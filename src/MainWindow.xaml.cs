using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.IO;
using System.Globalization;
using Degausser.Utils;
using Microsoft.Win32;

namespace Degausser
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        Karaoke karaoke = new Karaoke();
        public static RoutedCommand OpenCommand = new RoutedCommand();
        public static RoutedCommand ImportCommand = new RoutedCommand();
        public static RoutedCommand ExportCommand = new RoutedCommand();
        public static RoutedCommand RefreshCommand = new RoutedCommand();
        public static RoutedCommand PlayPauseCommand = new RoutedCommand();
        public OpenFileDialog openFileDialog = new OpenFileDialog { Filter = "Jb Manager File|mgr.bin" };

        JbManager jbManager;

        public MainWindow()
        {
            InitializeComponent();

            //SetTestDirectory();
            //CompareSomeKnownSimilarFiles();
            //LookForSimilarSongs();
            //TestBDXAssertions();



            //Directory.SetCurrentDirectory(@"C:\Users\Adib\Documents\Daigasso\BDX Mega Pack 2.4");

            Logging.OnMessage += (s, e) => Dispatcher.BeginInvoke(new Action(() =>
            {
                txtLogView.AppendText($"{e}\n");
                txtLogView.ScrollToEnd();
            }));
            Logging.Log($"Current directory: {Directory.GetCurrentDirectory()}");

            Refresh(this, null); // populate library

            lstMediaLibrary.MouseDoubleClick += (s, e) =>
            {
                PlayItem((e.OriginalSource as FrameworkElement)?.DataContext as BBPRecord);
            };

            lstSaveEditor.MouseDoubleClick += (s, e) =>
            {
                PlayItem((e.OriginalSource as FrameworkElement)?.DataContext as BBPRecord);
            };

            lstMediaLibrary.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    PlayItem((s as SortableListView)?.SelectedItem as BBPRecord);
                }
            };

            MidiPlayer.Instance.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == "Position")
                {
                    Dispatcher.BeginInvoke(new Action(() => UpdateKaraoke()));
                }
            };

            lstSaveEditor.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Delete)
                {
                    Delete();
                }
            };

        }

        void PlayItem(BBPRecord record)
        {
            if (record == null) return;
            karaoke = new Karaoke(record.bbp);
            karaokeEffect.PositionStart = 0;
            karaokeEffect.PositionCount = 0;
            karaokeViewer.ScrollToTop();
            karaokeBlock.Text = karaoke.Lyrics;
            MidiPlayer.Instance.Play(record.GetMidiData());
        }

        void UpdateKaraoke()
        {
            if (karaoke.IsEnabled)
            {
                karaoke.Position = MidiPlayer.Instance.Position;
                karaokeEffect.PositionStart = karaoke.PositionStart;
                karaokeEffect.PositionCount = karaoke.PositionCount;
                //karaokeFraction.Offset = karaoke.PositionFraction; // causes lag??
            }
        }

        void Open(string path)
        {
            jbManager = new JbManager(path);
            Title = $"Degausser - {path}";

            Logging.Log($"Opened {path}");

            var lst = new List<BBPRecord>();
            for (int i = 0; i < jbManager.mgr.Items.Length; i++)
            {
                var item = jbManager.mgr.Items[i];
                if (item.ID != 0xFFFFFFFF && item.Flags.OnSD)
                {
                    var idPath = Path.Combine(jbManager.Directory, $"gak\\{item.ID:x8}\\pack");
                    lst.Add(new BBPRecord(item, idPath, i));
                }
            }
            lstSaveEditor.ItemsSource = lst;
            // add another overload of BBPRecord to take stuff
        }

        void Open(object s, RoutedEventArgs e)
        {
            if (openFileDialog.ShowDialog() == true)
            {
                Open(Path.GetFullPath(openFileDialog.FileName));
            }
        }

        void Import(object s, RoutedEventArgs e)
        {
            if (jbManager == null)
            {
                Logging.Log("Please open a mgr.bin file first before importing");
            }
            else if (lstMediaLibrary.SelectedItems.Count == 0)
            {
                Logging.Log("Please select some items to import");
            }
            else
            {
                var mgr = jbManager.mgr;
                var set = new HashSet<uint>(mgr.Items.Select(x => x.ID));
                var items = lstMediaLibrary.SelectedItems.Cast<BBPRecord>().Select(record =>
                {
                    try
                    {
                        int n = Enumerable.Range(0, 3700).First(i => mgr.Items[i].ID == 0xFFFFFFFF);

                        var item = record.mgrItem;
                        byte[] packdata;
                        if ((item.ID >> 16) == 0x8000)
                        {
                            var newID = Enumerable.Range(0, 65536).Select(i => (uint)(i + 0x80000000)).First(i => !set.Contains(i));
                            item.ID = newID;
                            record.bbp.titleID = (int)newID;
                        }
                        else if (set.Contains(item.ID))
                        {
                            return JbMgrFormat.JbMgrItem.Empty;
                        }

                        packdata = record.bbp.StructToArray();
                        var idPath = Path.Combine(jbManager.Directory, $"gak\\{item.ID:x8}\\");
                        Directory.CreateDirectory(idPath);
                        record.SaveAsPackFile(Path.Combine(idPath, "pack"));
                        mgr.Items[n] = item;
                        set.Add(item.ID);

                        return item;
                    }
                    catch (IOException)
                    {
                        return JbMgrFormat.JbMgrItem.Empty;
                    }
                })
                .ToList();

                var count = items.Count(item => item.ID != 0xFFFFFFFF);

                jbManager.Save();
                Open(jbManager.FilePath);
                //mgr.Write(mgrpath);
                //mgr.Write(Path.Combine(Path.GetDirectoryName(mgrpath), "mgr_.bin"));
                //OpenMgrBin(frm, mgrpath);
                if (count == lstMediaLibrary.SelectedItems.Count)
                {
                    Logging.Log($"Successfully imported all {count} songs");
                }
                else
                {
                    Logging.Log($"Only managed to import {count} of {lstMediaLibrary.SelectedItems.Count} songs. The following files had errors:");
                    foreach (var pair in lstMediaLibrary.SelectedItems.Cast<BBPRecord>().Zip(items, Tuple.Create))
                    {
                        if (pair.Item2.ID == 0xFFFFFFFF)
                        {
                            Logging.Log($"- {pair.Item1.FullPath}");
                        }
                    }
                }
            }
        }

        void Export(object s, RoutedEventArgs e)
        {
            if (jbManager == null)
            {
                Logging.Log("Please open a mgr.bin file first before exporting");
            }
            else if (lstSaveEditor.SelectedItems.Count == 0)
            {
                Logging.Log("Please select some items to export");
            }
            else
            {
                Directory.CreateDirectory("Ripped");
                var errors = lstSaveEditor.SelectedItems.Cast<BBPRecord>().Where(record =>
                {
                    try
                    {
                        var item = record.mgrItem;
                        string bbpPath = Path.Combine("Ripped", $"{item.Title.Replace("\n", "")} ({item.Author}).bbp".IOFriendly());
                        record.SaveAsBBPFile(bbpPath);
                        ((List<BBPRecord>)lstMediaLibrary.ItemsSource).Add(new BBPRecord(bbpPath));
                        return false;
                    }
                    catch (IOException)
                    {
                        return true;
                    }
                })
                .ToList();
                lstMediaLibrary.Items.Refresh();
                Logging.Log($"Successfully exported {lstSaveEditor.SelectedItems.Count - errors.Count} of {lstSaveEditor.SelectedItems.Count}");
            }
        }

        void Refresh(object s, RoutedEventArgs e)
        {
            lstMediaLibrary.ItemsSource = null;
            lstMediaLibrary.IsEnabled = false;
            var paths = (from path in Directory.EnumerateFiles(Directory.GetCurrentDirectory(), "*.*", SearchOption.AllDirectories)
                         let ext = path.Substring(path.Length - 4).ToLower()
                         where ext == ".bdx" || ext == ".bbp" || ext == ".bin"
                         select path).ToList();
            lstMediaLibrary.Items.Add(new { Filename = $"Populating list... please wait (approximately {paths.Count} items)" });

            Task.Factory.StartNew(() => (from path in paths select new BBPRecord(path)).SkipExceptions(true).ToList())
                .ContinueWith(t =>
                {
                    lstMediaLibrary.Items.Clear();
                    lstMediaLibrary.ItemsSource = t.Result;
                    lstMediaLibrary.IsEnabled = true;
                    Logging.Log($"Media library population complete: added {t.Result.Count} items to list");
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void PlayPause(object s, RoutedEventArgs e)
        {
            if (MidiPlayer.Instance.State == MidiPlayer.MidiPlayerState.Playing)
            {
                MidiPlayer.Instance.State = MidiPlayer.MidiPlayerState.Paused;
            }
            else
            {
                MidiPlayer.Instance.Play();
            }
        }

        void Delete()
        {
            if (jbManager == null)
            {
                return;
            }
            else if (lstSaveEditor.SelectedItems.Count == 0)
            {
                return;
            }

            var count = lstSaveEditor.SelectedItems.Cast<BBPRecord>().Count(record =>
            {
                try
                {
                    var idPath = Path.Combine(jbManager.Directory, $"gak\\{record.mgrItem.ID:x8}");
                    Directory.Delete(idPath, true);
                    jbManager.mgr.Items[record.Slot] = JbMgrFormat.JbMgrItem.Empty;
                    return true;
                }
                catch (IOException)
                {
                    Logging.Log($"Failed to delete Slot {record.Slot}: {record.Title}");
                    return false;
                }
            });
            jbManager.Save();

            Logging.Log($"Deleted {count} of {lstSaveEditor.SelectedItems.Count} records");
            Open(jbManager.FilePath);
        }
    }
}
