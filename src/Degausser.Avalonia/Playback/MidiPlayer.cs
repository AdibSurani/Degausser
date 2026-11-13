using System.ComponentModel;
using Avalonia.Threading;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Multimedia;

namespace Degausser;

/// <summary>
/// Desktop playback adapter. MIDI construction and all Band Brothers
/// interpretation live in Degausser.Core. SF2/MeltySynth is the preferred
/// cross-platform backend; native system MIDI remains a fallback when no
/// SoundFont is configured on platforms supported by DryWetMIDI.
/// </summary>
public sealed class MidiPlayer : INotifyPropertyChanged, IDisposable
{
    public static MidiPlayer Instance { get; } = new();

    private readonly DispatcherTimer _positionTimer;
    private Playback? _playback;
    private OutputDevice? _outputDevice;
    private SoundFontPlayback? _soundFontPlayback;
    private BbpMidiDocument? _currentDocument;
    private int _position;
    private int _length;
    private int _ticksPerFrame = 1;
    private TempoMap? _tempoMap;
    private ushort _volume = ushort.MaxValue;
    private MidiChannel[] _channels = CreatePlaceholderChannels();
    private string _availabilityMessage = string.Empty;

    private MidiPlayer()
    {
        _positionTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(20)
        };
        _positionTimer.Tick += (_, _) => RefreshPlaybackState();
    }

    public sealed class MidiChannel : INotifyPropertyChanged
    {
        private bool _isActive = true;

        internal MidiChannel(BbpMidiChannelInfo info)
        {
            SourceChannel = info.SourceChannel;
            PlaybackChannel = info.PlaybackChannel;
            IsDrum = info.IsDrum;
            InstrumentName = info.IsUnused ? "Unused" : info.InstrumentName;
            IsAvailable = !info.IsUnused;
            _isActive = IsAvailable;
        }

        private MidiChannel(int sourceChannel, string name)
        {
            SourceChannel = sourceChannel;
            PlaybackChannel = sourceChannel == 9 ? (byte)10 : (byte)sourceChannel;
            InstrumentName = name;
            IsAvailable = false;
            _isActive = false;
        }

        public int SourceChannel { get; }
        public byte PlaybackChannel { get; }
        public bool IsDrum { get; }
        public string InstrumentName { get; }
        public int PartNumber => SourceChannel + 1;
        public bool IsAvailable { get; }

        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (!IsAvailable && value) return;
                if (_isActive == value) return;
                _isActive = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        internal static MidiChannel Placeholder(int sourceChannel) => new(sourceChannel, "Unused");
    }

    public MidiChannel[] Channels => _channels;

    public int Position
    {
        get => _position;
        set
        {
            int target = Math.Clamp(value, 0, Length);
            if (_soundFontPlayback is not null)
                SeekSoundFont(_soundFontPlayback, GetMetricTime(target));
            else if (_playback is not null)
                _playback.MoveToTime(new MidiTimeSpan((long)target * _ticksPerFrame));
            SetPosition(target);
        }
    }

    // Seeking re-runs the song up to the target, so it happens on a worker. While a
    // seek is in flight, newer targets (slider drag) replace each other and only the
    // last one is run.
    private readonly object _seekGate = new();
    private TimeSpan? _pendingSeek;
    private bool _seekRunning;

    private void SeekSoundFont(SoundFontPlayback playback, TimeSpan target)
    {
        lock (_seekGate)
        {
            _pendingSeek = target;
            if (_seekRunning) return;
            _seekRunning = true;
        }

        _ = Task.Run(() =>
        {
            while (true)
            {
                TimeSpan next;
                lock (_seekGate)
                {
                    if (_pendingSeek is not { } pending)
                    {
                        _seekRunning = false;
                        return;
                    }
                    next = pending;
                    _pendingSeek = null;
                }

                try { playback.Seek(next); }
                catch (ObjectDisposedException) { /* song changed or player closed mid-seek */ }
                catch (Exception ex) { Logging.Log($"Seek failed: {ex.Message}"); }
            }
        });
    }

    public int Length => _length;

    // A slider whose maximum is 0 rests at its right end; keep the thumb at the start when nothing is loaded.
    public int SeekMaximum => Math.Max(_length, 1);
    public string PositionTimeText => FormatClock(GetMetricTime(Position));
    public string LengthTimeText => FormatClock(GetMetricTime(Length));
    public string AvailabilityMessage => _availabilityMessage;

    public ushort Volume
    {
        get => _volume;
        set
        {
            if (_volume == value) return;
            _volume = value;
            NotifyPropertyChanged(nameof(Volume));
        }
    }

    public bool IsPlaying
    {
        get => _soundFontPlayback?.IsPlaying == true || _playback?.IsRunning == true;
        set
        {
            if (value) Play();
            else Stop();
        }
    }

    public bool CanPlay => _soundFontPlayback is not null || _playback is not null;

    public void Play(BbpMidiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _currentDocument = document;
        StopAndDisposePlayback();

        _channels = document.Channels.Select(info => new MidiChannel(info)).ToArray();
        _length = document.LengthFrames;
        _ticksPerFrame = document.TicksPerFrame;
        _tempoMap = document.PlaybackFile.GetTempoMap();
        _position = 0;
        NotifyPropertyChanged(nameof(Channels));
        NotifyPropertyChanged(nameof(Length));
        NotifyPropertyChanged(nameof(SeekMaximum));
        NotifyPropertyChanged(nameof(LengthTimeText));
        NotifyPropertyChanged(nameof(Position));
        NotifyPropertyChanged(nameof(PositionTimeText));

        if (!TryCreateSoundFontPlayback(document) && !EnsureOutputDevice())
        {
            NotifyPropertyChanged(nameof(CanPlay));
            return;
        }

        if (_soundFontPlayback is null)
        {
            _playback = document.PlaybackFile.GetPlayback(_outputDevice!);
            _playback.TrackNotes = true;
            _playback.NoteCallback = ApplyChannelState;
        }

        NotifyPropertyChanged(nameof(CanPlay));
        Play();
    }

    public void ReloadPlaybackSettings()
    {
        if (_currentDocument is null) return;

        BbpMidiDocument document = _currentDocument;
        int oldPosition = _position;
        bool wasPlaying = IsPlaying;
        Play(document);
        Position = oldPosition;
        if (!wasPlaying)
            Stop();
    }

    public void Play()
    {
        if (_soundFontPlayback is not null)
        {
            if (_soundFontPlayback.IsPlaying) return;
            if (_position >= _length)
            {
                _soundFontPlayback.Seek(TimeSpan.Zero);
                SetPosition(0);
            }
            _soundFontPlayback.Start();
        }
        else
        {
            if (_playback is null || _playback.IsRunning) return;
            if (_position >= _length)
            {
                _playback.MoveToStart();
                SetPosition(0);
            }
            _playback.Start();
        }

        _positionTimer.Start();
        NotifyPropertyChanged(nameof(IsPlaying));
    }

    public void Stop()
    {
        if (_soundFontPlayback is not null)
        {
            _soundFontPlayback.Pause();
        }
        else if (_playback is not null && _playback.IsRunning)
        {
            _playback.Stop();
        }

        RefreshPlaybackState();
        NotifyPropertyChanged(nameof(IsPlaying));
    }

    public void Close() => Dispose();

    public void Dispose()
    {
        _positionTimer.Stop();
        StopAndDisposePlayback();
        _outputDevice?.Dispose();
        _outputDevice = null;
        _currentDocument = null;
        GC.SuppressFinalize(this);
    }

    private bool TryCreateSoundFontPlayback(BbpMidiDocument document)
    {
        string path = (UserSettingsStore.Current.SoundFontPath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            string automatic = UserSettingsStore.GetDefaultSoundFontPath();
            if (!string.IsNullOrWhiteSpace(automatic))
                path = automatic;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            if (OperatingSystem.IsLinux())
                SetAvailability("Choose an SF2 SoundFont in Settings for preview playback. MIDI export is still available.");
            return false;
        }

        if (!File.Exists(path))
        {
            SetAvailability($"SoundFont not found: {path}");
            Logging.Log(AvailabilityMessage);
            return false;
        }

        try
        {
            bool faithful = UserSettingsStore.Current.SoundFontMapping == SoundFontMappingMode.BbpFaithful;
            _soundFontPlayback = new SoundFontPlayback(
                path,
                document,
                faithful,
                IsSourceChannelActive,
                () => _volume);
            SetAvailability(string.Empty);
            // Once per SoundFont and mapping, not once per song.
            string description = $"SF2 playback: {Path.GetFileName(path)} ({(faithful ? "BBP-faithful" : "General MIDI")})";
            if (description != _lastSoundFontLog)
                Logging.Log(_lastSoundFontLog = description);
            return true;
        }
        catch (Exception ex)
        {
            _soundFontPlayback?.Dispose();
            _soundFontPlayback = null;
            SetAvailability($"Could not start SoundFont playback: {ex.Message}");
            Logging.Log(AvailabilityMessage);
            return false;
        }
    }

    private string? _lastSoundFontLog;

    private bool IsSourceChannelActive(int sourceChannel)
        => (uint)sourceChannel < (uint)_channels.Length && _channels[sourceChannel].IsActive;

    private NotePlaybackData ApplyChannelState(NotePlaybackData rawNoteData, long rawTime, long rawLength, TimeSpan playbackTime)
    {
        byte rawChannel = (byte)rawNoteData.Channel;
        MidiChannel? source = _channels.FirstOrDefault(c => c.PlaybackChannel == rawChannel);
        if (source is null || !source.IsActive)
            return null!;

        byte velocity = (byte)rawNoteData.Velocity;
        // Native-system MIDI is only a fallback: it has no post-synth mixer hook,
        // so approximate BBP master + UI gain at Note On. The preferred SF2 path
        // applies both after synthesis and therefore affects sustained voices too.
        // MasterPlaybackGainApproximation intentionally follows the hardware-
        // verified visible multiplier until the native DSP law is recovered.
        double fallbackGain = (_currentDocument?.MasterPlaybackGainApproximation ?? 1.0)
            * (_volume / (double)ushort.MaxValue);
        byte scaledVelocity = (byte)Math.Clamp(
            (int)Math.Round(velocity * Math.Sqrt(Math.Max(0.0, fallbackGain))), 0, 127);
        byte outputChannel = source.IsDrum ? (byte)9 : rawChannel;

        return new NotePlaybackData(
            rawNoteData.NoteNumber,
            (SevenBitNumber)scaledVelocity,
            rawNoteData.OffVelocity,
            (FourBitNumber)outputChannel);
    }

    private bool EnsureOutputDevice()
    {
        if (_outputDevice is not null)
        {
            SetAvailability(string.Empty);
            return true;
        }

        if (OperatingSystem.IsLinux())
        {
            if (string.IsNullOrWhiteSpace(AvailabilityMessage))
                SetAvailability("Choose an SF2 SoundFont in Settings for preview playback. MIDI export is still available.");
            Logging.Log(AvailabilityMessage);
            return false;
        }

        try
        {
            if (OutputDevice.GetDevicesCount() == 0)
            {
                SetAvailability("No MIDI output device is available. Choose an SF2 SoundFont in Settings instead.");
                Logging.Log(AvailabilityMessage);
                return false;
            }

            _outputDevice = OutputDevice.GetByIndex(0);
            _outputDevice.PrepareForEventsSending();
            SetAvailability(string.Empty);
            return true;
        }
        catch (Exception ex)
        {
            SetAvailability($"Could not open MIDI output device: {ex.Message}");
            Logging.Log(AvailabilityMessage);
            _outputDevice?.Dispose();
            _outputDevice = null;
            return false;
        }
    }

    private void SetAvailability(string value)
    {
        if (_availabilityMessage == value) return;
        _availabilityMessage = value;
        NotifyPropertyChanged(nameof(AvailabilityMessage));
    }

    private void RefreshPlaybackState()
    {
        if (_soundFontPlayback is not null)
        {
            // The worker holds the playback lock for the whole seek, and the old position
            // would pull the slider back while it is being dragged.
            lock (_seekGate)
            {
                if (_seekRunning) return;
            }

            TimeSpan current = _soundFontPlayback.Position;
            bool ended = _soundFontPlayback.EndOfSequence;
            SetPosition(ended ? _length : FrameAtMetricTime(current));
            NotifyPropertyChanged(nameof(IsPlaying));
            if (!_soundFontPlayback.IsPlaying && (ended || _position >= _length))
                _positionTimer.Stop();
            return;
        }

        if (_playback is null) return;

        long currentTicks = _playback.GetCurrentTime<MidiTimeSpan>().TimeSpan;
        long currentFrame = currentTicks / Math.Max(1, _ticksPerFrame);
        SetPosition((int)Math.Clamp(currentFrame, 0, _length));
        NotifyPropertyChanged(nameof(IsPlaying));

        if (!_playback.IsRunning && _position >= _length)
            _positionTimer.Stop();
    }

    private void StopAndDisposePlayback()
    {
        if (_soundFontPlayback is not null)
        {
            _soundFontPlayback.Dispose();
            _soundFontPlayback = null;
        }

        if (_playback is not null)
        {
            if (_playback.IsRunning)
                _playback.Stop();
            _playback.Dispose();
            _playback = null;
        }

        _positionTimer.Stop();
        NotifyPropertyChanged(nameof(IsPlaying));
        NotifyPropertyChanged(nameof(CanPlay));
    }

    private void SetPosition(int value)
    {
        if (_position == value) return;
        _position = value;
        NotifyPropertyChanged(nameof(Position));
        NotifyPropertyChanged(nameof(PositionTimeText));
    }

    private TimeSpan GetMetricTime(int frame)
    {
        if (_tempoMap is null || frame <= 0)
            return TimeSpan.Zero;

        long ticks = (long)frame * Math.Max(1, _ticksPerFrame);
        return TimeConverter.ConvertTo<MetricTimeSpan>(ticks, _tempoMap);
    }

    private int FrameAtMetricTime(TimeSpan time)
    {
        if (_tempoMap is null || time <= TimeSpan.Zero || _length <= 0)
            return 0;

        if (time >= GetMetricTime(_length))
            return _length;

        int low = 0;
        int high = _length;
        while (low < high)
        {
            int mid = low + (high - low + 1) / 2;
            if (GetMetricTime(mid) <= time)
                low = mid;
            else
                high = mid - 1;
        }
        return low;
    }

    private static string FormatClock(TimeSpan time)
    {
        int totalSeconds = Math.Max(0, (int)Math.Floor(time.TotalSeconds + 1e-9));
        int hours = totalSeconds / 3600;
        int minutes = (totalSeconds / 60) % 60;
        int seconds = totalSeconds % 60;
        return hours > 0 ? $"{hours}:{minutes:00}:{seconds:00}" : $"{minutes}:{seconds:00}";
    }

    private static MidiChannel[] CreatePlaceholderChannels()
        => Enumerable.Range(0, 10).Select(MidiChannel.Placeholder).ToArray();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void NotifyPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
