using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.ComponentModel;

namespace Degausser
{
    /// <summary>
    /// Interaction logic for SortableListView.xaml
    /// </summary>
    public partial class SortableListView : ListView
    {
        public SortableListView()
        {
            InitializeComponent();

            GridViewColumnHeader prevHeader = null;
            var direction = ListSortDirection.Ascending;

            AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler((s, e) =>
            {
                var header = e.OriginalSource as GridViewColumnHeader;
                if (header == null || header.Role == GridViewColumnHeaderRole.Padding) return;

                if (header == prevHeader && direction == ListSortDirection.Ascending)
                {
                    direction = ListSortDirection.Descending;
                }
                else
                {
                    direction = ListSortDirection.Ascending;
                }

                var dataView = CollectionViewSource.GetDefaultView(ItemsSource);
                dataView.SortDescriptions.Clear();
                var sortBy = ((Binding)header.Column.DisplayMemberBinding)?.Path?.Path;
                if (sortBy == null) return;
                dataView.SortDescriptions.Add(new SortDescription(sortBy, direction));
                dataView.Refresh();

                header.Column.HeaderTemplate = (DataTemplate)FindResource("HeaderTemplate" + direction);

                // Remove arrow from previously sorted header
                if (prevHeader != null && prevHeader != header)
                {
                    prevHeader.Column.HeaderTemplate = null;
                }
                prevHeader = header;
            }));
        }
    }
}
