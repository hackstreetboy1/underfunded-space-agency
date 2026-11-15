using Godot;
using Usa.Game.Services.Audio;
using Usa.Game.Services.Logging;

namespace Usa.Game.Services.Voice;

/// <summary>
/// Plays crew speech (SCRIPTING.md 13.3). Each speaker gets an <see cref="AudioStreamPlayer"/> on its own bus
/// <c>Speech_&lt;crewId&gt;</c> (sending to <c>Speech</c>, so the audio owner's Speech processing still applies)
/// with a spectrum analyser that feeds <see cref="GetOutputLevel"/> for lip-flap. Lines queue per speaker;
/// <see cref="PlaybackStarted"/> fires when audio starts so captions appear in sync.
/// </summary>
public sealed partial class SpeechPlayer : Node
{
    private const string Category = "SpeechPlayer";
    private const string BusPrefix = "Speech_";
    private const float LipFlapMinHz = 80f;
    private const float LipFlapMaxHz = 4000f;

    private readonly Dictionary<string, Channel> _channels = new(StringComparer.Ordinal);
    private Log? _log;
    private bool _paused;
    private bool _frozen;

    public event Action<SpeechLine>? PlaybackStarted;

    public event Action<SpeechFinished>? PlaybackFinished;

    /// <summary>True while held by <see cref="SetPaused"/> (connection outage) or <see cref="SetFrozen"/> (any game freeze).</summary>
    public bool Paused => _paused || _frozen;

    public void Initialize(Log log) => _log = log;

    public override void _Ready() => AudioBuses.Ensure(AudioBuses.Speech);

    public void Enqueue(SpeechLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var channel = GetChannel(line.CrewId);

        if (line.Priority == SpeechPriority.Critical)
        {
            var node = channel.Pending.First;
            while (node is not null && node.Value.Priority == SpeechPriority.Critical) node = node.Next;
            if (node is null) channel.Pending.AddLast(line);
            else channel.Pending.AddBefore(node, line);

            if (channel.Current?.Priority == SpeechPriority.Chatter) EndCurrent(channel, SpeechEndReason.Interrupted);
        }
        else
        {
            channel.Pending.AddLast(line);
        }

        if (channel.Current is null) StartNext(channel);
    }

    public bool IsSpeaking(string crewId) => _channels.TryGetValue(crewId, out var channel) && channel.Current is not null;

    /// <summary>Lines waiting behind the current one for <paramref name="crewId"/>.</summary>
    public int QueuedCount(string crewId) => _channels.TryGetValue(crewId, out var channel) ? channel.Pending.Count : 0;

    /// <summary>
    /// How long the current line's caption should stay up, counted from its <see cref="PlaybackStarted"/> (read it in that
    /// handler): the clip's length, or the reading time for a caption-only line, plus the 1.5 s tail
    /// (<see cref="CaptionTiming.HoldSeconds"/>). Playback time held by a pause or freeze comes on top. 0 when silent.
    /// </summary>
    public double CaptionHoldSeconds(string crewId) =>
        _channels.TryGetValue(crewId, out var channel) && channel.Current is not null ? channel.CaptionHoldSeconds : 0;

    /// <summary>Stops the speaker and drops their queue.</summary>
    public void Stop(string crewId)
    {
        if (!_channels.TryGetValue(crewId, out var channel)) return;
        channel.Pending.Clear();
        if (channel.Current is not null) EndCurrent(channel, SpeechEndReason.Stopped);
    }

    public void StopAll()
    {
        foreach (var crewId in _channels.Keys.ToArray()) Stop(crewId);
    }

    /// <summary>Holds playback in place (connection outage) and resumes it where it left off.</summary>
    public void SetPaused(bool paused)
    {
        if (_paused == paused) return;
        var wasPaused = Paused;
        _paused = paused;
        ApplyPause(wasPaused);
    }

    /// <summary>
    /// Follows the app's one freeze signal (Esc menu, outage, loading). Independent of <see cref="SetPaused"/>: playback
    /// resumes only when neither holds it, so an outage that ends under the Esc menu stays silent.
    /// </summary>
    public void SetFrozen(bool frozen)
    {
        if (_frozen == frozen) return;
        var wasPaused = Paused;
        _frozen = frozen;
        ApplyPause(wasPaused);
    }

    private void ApplyPause(bool wasPaused)
    {
        var paused = Paused;
        if (paused == wasPaused) return;
        foreach (var channel in _channels.Values)
        {
            channel.Player.StreamPaused = paused;
            channel.CaptionTimer.Paused = paused;
            if (!paused && channel.Current is null) StartNext(channel);
        }
    }

    /// <summary>Speech loudness 0..1 for the speaker's lip-flap; 0 when silent or not speaking.</summary>
    public float GetOutputLevel(string crewId)
    {
        if (!_channels.TryGetValue(crewId, out var channel) || !channel.Player.Playing || channel.Player.StreamPaused) return 0f;

        var bus = AudioServer.GetBusIndex(channel.BusName);
        if (bus < 0) return 0f;
        if (AudioServer.GetBusEffectInstance(bus, channel.AnalyzerSlot) is not AudioEffectSpectrumAnalyzerInstance analyzer) return 0f;

        var magnitude = analyzer.GetMagnitudeForFrequencyRange(LipFlapMinHz, LipFlapMaxHz);
        return PcmLevel.ToMeter(Math.Max(magnitude.X, magnitude.Y));
    }

    private Channel GetChannel(string crewId)
    {
        if (_channels.TryGetValue(crewId, out var existing)) return existing;

        var busName = BusPrefix + crewId;
        var bus = AudioBuses.Ensure(busName, sendTo: AudioBuses.Speech);
        var (_, slot) = AudioBuses.EnsureEffect(bus, () => new AudioEffectSpectrumAnalyzer
        {
            FftSize = AudioEffectSpectrumAnalyzer.FftSizeEnum.Size512,
            BufferLength = 0.1f,
        });

        var player = new AudioStreamPlayer { Name = $"Voice_{crewId}", Bus = busName };
        var timer = new Godot.Timer { Name = $"Caption_{crewId}", OneShot = true };
        AddChild(player);
        AddChild(timer);

        var channel = new Channel(crewId, busName, slot, player, timer);
        player.Finished += () => OnLineEnded(channel);
        timer.Timeout += () => OnLineEnded(channel);
        _channels.Add(crewId, channel);
        return channel;
    }

    private void StartNext(Channel channel)
    {
        if (Paused || channel.Pending.First is null) return;

        var line = channel.Pending.First.Value;
        channel.Pending.RemoveFirst();
        channel.Current = line;

        var stream = Decode(line);
        if (stream is not null)
        {
            channel.Player.Stream = stream;
            channel.Player.Play();
        }
        else
        {
            channel.CaptionTimer.Start(CaptionTiming.CaptionOnlySeconds(line.Caption));
        }
        channel.CaptionHoldSeconds = CaptionTiming.HoldSeconds(stream?.GetLength(), line.Caption);
        PlaybackStarted?.Invoke(line);
    }

    private void OnLineEnded(Channel channel)
    {
        if (channel.Current is null) return;
        EndCurrent(channel, SpeechEndReason.Completed);
    }

    private void EndCurrent(Channel channel, SpeechEndReason reason)
    {
        var line = channel.Current!;
        channel.Current = null;
        channel.Player.Stop();
        channel.CaptionTimer.Stop();
        PlaybackFinished?.Invoke(new SpeechFinished(line, reason));
        StartNext(channel);
    }

    private AudioStreamMP3? Decode(SpeechLine line)
    {
        if (line.Mp3 is not { Length: > 0 }) return null;
        var stream = new AudioStreamMP3 { Data = line.Mp3 };
        if (stream.GetLength() > 0) return stream;

        _log?.Warn(Category, $"undecodable MP3 for {line.CrewId} line={line.LineId ?? "-"}; showing caption only");
        return null;
    }

    private sealed class Channel(string crewId, string busName, int analyzerSlot, AudioStreamPlayer player, Godot.Timer captionTimer)
    {
        public string CrewId { get; } = crewId;

        public string BusName { get; } = busName;

        public int AnalyzerSlot { get; } = analyzerSlot;

        public AudioStreamPlayer Player { get; } = player;

        public Godot.Timer CaptionTimer { get; } = captionTimer;

        public LinkedList<SpeechLine> Pending { get; } = new();

        public SpeechLine? Current { get; set; }

        public double CaptionHoldSeconds { get; set; }
    }
}
