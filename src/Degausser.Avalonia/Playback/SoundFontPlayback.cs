using Melanchall.DryWetMidi.Core;
using DwmFile = Melanchall.DryWetMidi.Core.MidiFile;
using MeltySynth;
using MiniAudioEx.Core.AdvancedAPI;
using MiniAudioEx.Native;

namespace Degausser;

/// <summary>
/// Cross-platform SF2 playback. MeltySynth owns MIDI sequencing/synthesis;
/// MiniAudioEx only provides the platform audio callback. MeltySynth has no MIDI
/// ports, so the playback MIDI is split per port and each port gets its own
/// synthesizer (sharing one SoundFont); the outputs are summed. That gives every
/// drum part its own percussion channel, as in the exported multi-port MIDI.
/// </summary>
internal sealed unsafe class SoundFontPlayback : IDisposable
{
    private const int SampleRate = 44100;
    private const int OutputChannels = 2;
    private const int SeekBlockFrames = 4096;

    private static readonly ma_device_data_proc DeviceDataCallback = OnDeviceData;
    private static SoundFontPlayback? activeInstance;

    private readonly object gate = new();
    private readonly PortVoice[] voices;
    private readonly IReadOnlyDictionary<int, BbpMidiChannelInfo> channelByPlaybackChannel;
    private readonly Func<int, bool> isSourceChannelActive;
    private readonly Func<ushort> getVolume;
    private readonly double masterPlaybackGain;
    private readonly bool useFaithfulBbpBanks;
    private readonly float[] seekBuffer = new float[SeekBlockFrames * OutputChannels];
    private readonly float[] mixBuffer = new float[8192 * OutputChannels];
    private readonly MaDevice device;

    private bool isPlaying;
    private bool disposed;
    // True while Seek fast-forwards: new notes are dropped so the synth only chases
    // controller/program state and has no voices to render (a 3-minute seek of a dense
    // song otherwise costs seconds on the UI thread).
    private bool chasing;

    public SoundFontPlayback(
        string soundFontPath,
        BbpMidiDocument document,
        bool useFaithfulBbpBanks,
        Func<int, bool> isSourceChannelActive,
        Func<ushort> getVolume)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(soundFontPath);
        ArgumentNullException.ThrowIfNull(document);
        this.useFaithfulBbpBanks = useFaithfulBbpBanks;
        this.isSourceChannelActive = isSourceChannelActive ?? throw new ArgumentNullException(nameof(isSourceChannelActive));
        this.getVolume = getVolume ?? throw new ArgumentNullException(nameof(getVolume));
        // MeltySynth playback renders the MIDI channel messages but does not
        // implement SpessaSynth's global SysEx/master law. Apply the same
        // reserved-headroom amplitude gain externally once; never use 1.5x
        // in addition to an interpreted SysEx control in the same signal path.
        masterPlaybackGain = document.MasterPlaybackGainApproximation / 1.5;
        channelByPlaybackChannel = document.Channels.ToDictionary(c => (int)c.PlaybackChannel);

        var soundFont = new SoundFont(soundFontPath);
        var settings = new SynthesizerSettings(SampleRate);
        voices = SplitByPort(document.PlaybackFile)
            .Select(split => new PortVoice(split.Port, soundFont, settings, split.File, this))
            .ToArray();
        foreach (PortVoice voice in voices)
            voice.Sequencer.Play(voice.Midi, false);

        device = new MaDevice();
        ma_device_config config = device.GetConfig(ma_device_type.playback);
        config.sampleRate = SampleRate;
        config.periodSizeInFrames = 1024;
        config.playback.format = ma_format.f32;
        config.playback.channels = OutputChannels;
        config.SetDataCallback(DeviceDataCallback);

        ma_result result = device.Initialize(config);
        if (result != ma_result.success)
        {
            device.Dispose();
            throw new InvalidOperationException($"MiniAudioEx could not initialize the default playback device ({result}).");
        }

        Volatile.Write(ref activeInstance, this);
        try
        {
            device.Start();
        }
        catch
        {
            Volatile.Write(ref activeInstance, null);
            device.Dispose();
            throw;
        }
    }

    public TimeSpan Position
    {
        get
        {
            lock (gate)
                return voices.Max(v => v.Sequencer.Position);
        }
    }

    public bool IsPlaying
    {
        get
        {
            lock (gate)
                return isPlaying;
        }
    }

    public bool EndOfSequence
    {
        get
        {
            lock (gate)
                return AllEnded;
        }
    }

    public void Start()
    {
        lock (gate)
        {
            ThrowIfDisposed();
            if (AllEnded)
                foreach (PortVoice voice in voices)
                    voice.Sequencer.Play(voice.Midi, false);
            isPlaying = true;
        }
    }

    public void Pause()
    {
        lock (gate)
            isPlaying = false;
    }

    public void Seek(TimeSpan target)
    {
        TimeSpan length = voices.Max(v => v.Midi.Length);
        if (target < TimeSpan.Zero)
            target = TimeSpan.Zero;
        if (target > length)
            target = length;

        lock (gate)
        {
            ThrowIfDisposed();
            bool resume = isPlaying;
            isPlaying = false;

            chasing = true;
            try
            {
                foreach (PortVoice voice in voices)
                {
                    MeltySynth.MidiFileSequencer sequencer = voice.Sequencer;
                    sequencer.Play(voice.Midi, false);
                    while (sequencer.Position < target && !sequencer.EndOfSequence)
                    {
                        double secondsRemaining = (target - sequencer.Position).TotalSeconds;
                        int frames = Math.Clamp((int)Math.Ceiling(secondsRemaining * SampleRate), 1, SeekBlockFrames);
                        sequencer.RenderInterleaved(seekBuffer.AsSpan(0, frames * OutputChannels));
                    }
                }
            }
            finally
            {
                chasing = false;
            }

            isPlaying = resume && !AllEnded;
        }
    }

    private bool AllEnded => voices.All(v => v.Sequencer.EndOfSequence);

    private static List<(byte Port, DwmFile File)> SplitByPort(DwmFile source)
    {
        // The first chunk is the tempo track; part tracks announce their port with a
        // port-prefix meta event (absent for single-port output, meaning port 0).
        List<TrackChunk> chunks = source.GetTrackChunks().ToList();
        var ports = new SortedDictionary<byte, List<TrackChunk>>();
        for (int i = 1; i < chunks.Count; i++)
        {
            byte port = chunks[i].Events.OfType<PortPrefixEvent>().FirstOrDefault()?.Port ?? 0;
            if (!ports.TryGetValue(port, out List<TrackChunk>? list))
                ports[port] = list = [];
            list.Add(chunks[i]);
        }
        if (ports.Count == 0)
            ports[0] = [];

        var result = new List<(byte, DwmFile)>();
        foreach ((byte port, List<TrackChunk> tracks) in ports)
        {
            var file = new DwmFile(new[] { chunks[0] }.Concat(tracks)) { TimeDivision = source.TimeDivision };
            result.Add((port, file));
        }
        return result;
    }

    private sealed class PortVoice
    {
        private readonly SoundFontPlayback owner;
        // Bank select is 14-bit in the MIDI (CC0 = MSB, CC32 = LSB) but MeltySynth reads CC0
        // alone, so the pair is held back and applied at the next program change.
        private readonly int[] bankMsb = new int[16];
        private readonly int[] bankLsb = new int[16];

        public PortVoice(byte port, SoundFont soundFont, SynthesizerSettings settings, DwmFile file, SoundFontPlayback owner)
        {
            this.owner = owner;
            Port = port;
            Synthesizer = new MeltySynth.Synthesizer(soundFont, settings);
            Sequencer = new MeltySynth.MidiFileSequencer(Synthesizer);
            Sequencer.OnSendMessage = Process;
            using var stream = new MemoryStream();
            file.Write(stream);
            stream.Position = 0;
            Midi = new MeltySynth.MidiFile(stream);
        }

        public byte Port { get; }
        public MeltySynth.Synthesizer Synthesizer { get; }
        public MeltySynth.MidiFileSequencer Sequencer { get; }
        public MeltySynth.MidiFile Midi { get; }

        private void Process(MeltySynth.Synthesizer synth, int channel, int command, int data1, int data2)
        {
            owner.channelByPlaybackChannel.TryGetValue(Port * 16 + channel, out BbpMidiChannelInfo? source);

            if (command == 0xB0 && data1 is 0 or 32)
            {
                if (data1 == 0) bankMsb[channel] = data2; else bankLsb[channel] = data2;
                return;
            }

            // Muting suppresses new attacks, but note-off messages still pass
            // through so toggling a part cannot leave a synth voice stuck on.
            if (command == 0x90 && data2 > 0
                && (owner.chasing || (source is not null && !owner.isSourceChannelActive(source.SourceChannel))))
                return;

            if (command == 0xC0)
            {
                int bank = (bankMsb[channel] << 7) | bankLsb[channel];
                int program = data1;
                if (owner.useFaithfulBbpBanks && source is { IsDrum: false, SoundFontProgram: byte sfProgram })
                {
                    bank = source.SoundFontBank ?? 0;
                    program = sfProgram;
                }
                // Channel 10 is MeltySynth's percussion channel: it adds 128 to the bank itself.
                if (channel == 9)
                    bank = Math.Max(bank - 128, 0);
                synth.ProcessMidiMessage(channel, 0xB0, 0, bank & 0x7F);
                synth.ProcessMidiMessage(channel, 0xB0, 32, 0);
                synth.ProcessMidiMessage(channel, 0xC0, program, 0);
                return;
            }

            synth.ProcessMidiMessage(channel, command, data1, data2);
        }
    }

    private void Render(Span<float> output)
    {
        lock (gate)
        {
            if (disposed || !isPlaying)
            {
                output.Clear();
                return;
            }

            if (voices.Length == 1)
            {
                voices[0].Sequencer.RenderInterleaved(output);
            }
            else
            {
                output.Clear();
                for (int offset = 0; offset < output.Length; offset += mixBuffer.Length)
                {
                    Span<float> block = output.Slice(offset, Math.Min(mixBuffer.Length, output.Length - offset));
                    Span<float> scratch = mixBuffer.AsSpan(0, block.Length);
                    foreach (PortVoice voice in voices)
                    {
                        voice.Sequencer.RenderInterleaved(scratch);
                        for (int i = 0; i < block.Length; i++)
                            block[i] += scratch[i];
                    }
                }
            }

            // BBP ALL/master applies a global gain after synthesis. For this
            // desktop MeltySynth path, use the SpessaSynth-calibrated effective
            // value (1+raw/128)/1.5. The exported MIDI has the equivalent
            // inverse-squared SysEx.
            double outputGain = masterPlaybackGain * (getVolume() / (double)ushort.MaxValue);
            if (outputGain != 1.0)
            {
                float gain = (float)outputGain;
                for (int i = 0; i < output.Length; i++)
                    output[i] *= gain;
            }

            if (AllEnded)
                isPlaying = false;
        }
    }

    private static unsafe void OnDeviceData(ma_device_ptr pDevice, IntPtr pOutput, IntPtr pInput, uint frameCount)
    {
        if (pOutput == IntPtr.Zero)
            return;

        int samples = checked((int)frameCount * OutputChannels);
        var output = new Span<float>((void*)pOutput, samples);
        SoundFontPlayback? instance = Volatile.Read(ref activeInstance);
        if (instance is null)
        {
            output.Clear();
            return;
        }
        try
        {
            instance.Render(output);
        }
        catch
        {
            // An exception on the audio thread would end the whole process.
            output.Clear();
            lock (instance.gate)
                instance.isPlaying = false;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            isPlaying = false;
            disposed = true;
        }

        if (ReferenceEquals(Volatile.Read(ref activeInstance), this))
            Volatile.Write(ref activeInstance, null);

        try { device.Stop(); }
        catch { /* device teardown should continue */ }
        device.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(SoundFontPlayback));
    }
}
