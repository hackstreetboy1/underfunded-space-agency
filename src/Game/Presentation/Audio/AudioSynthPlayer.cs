using Godot;
using Usa.Game.Presentation.Audio.Dsp;

namespace Usa.Game.Presentation.Audio;

/// <summary>
/// One voice: streams an <see cref="ISoundGenerator"/> through an <see cref="AudioStreamGenerator"/>.
/// The ring buffer is topped up every frame in fixed blocks; once the generator finishes, the voice
/// waits for the buffer to drain and then frees itself for reuse.
/// </summary>
public sealed partial class AudioSynthPlayer : AudioStreamPlayer
{
    private const int BlockFrames = 256;

    // ~80 ms of buffer rides out frame hitches while keeping UI and PTT latency low.
    private const float BufferSeconds = 0.08f;

    private readonly float[] _mono = new float[BlockFrames];
    private readonly Vector2[] _frames = new Vector2[BlockFrames];
    private AudioStreamGeneratorPlayback? _playback;
    private int _emptyBufferFrames;

    public ISoundGenerator? Generator { get; private set; }

    public bool IsActive => Generator is not null;

    public int Priority { get; private set; }

    public long StartSequence { get; private set; }

    public override void _EnterTree()
    {
        ProcessMode = ProcessModeEnum.Always;
        Stream ??= new AudioStreamGenerator
        {
            MixRateMode = AudioStreamGenerator.AudioStreamGeneratorMixRate.Custom,
            MixRate = AudioFormat.SampleRate,
            BufferLength = BufferSeconds,
        };
    }

    /// <summary>
    /// Drops the managed references to the generator stream and its playback. Left to the finalizer they outlive the
    /// engine on quit, and Godot reports them as leaked instances.
    /// </summary>
    public override void _ExitTree()
    {
        Silence();
        var stream = Stream;
        Stream = null;
        stream?.Dispose();
    }

    public override void _Process(double delta)
    {
        // Paused while the game is frozen: the buffer is not consumed, so there is nothing to top up.
        if (Generator is null || _playback is null || StreamPaused) return;
        if (!Generator.IsFinished)
        {
            Fill();
            return;
        }

        // Finished: keep playing until what is queued has been heard.
        if (_playback.GetFramesAvailable() >= _emptyBufferFrames) Silence();
    }

    public void Start(ISoundGenerator generator, StringName bus, float volumeDb, int priority, long startSequence)
    {
        ArgumentNullException.ThrowIfNull(generator);
        Silence();
        Generator = generator;
        Priority = priority;
        StartSequence = startSequence;
        Bus = bus;
        VolumeDb = volumeDb;
        StreamPaused = false;
        Play();
        _playback = (AudioStreamGeneratorPlayback)GetStreamPlayback();
        _emptyBufferFrames = _playback.GetFramesAvailable();
        Fill();
    }

    /// <summary>Graceful stop: the generator fades out on its own and the voice frees itself afterwards.</summary>
    public void Release() => Generator?.Stop();

    /// <summary>Immediate stop; used when the voice is stolen or reset.</summary>
    public void Silence()
    {
        if (Playing) Stop();
        Generator = null;
        _playback?.Dispose();
        _playback = null;
    }

    private void Fill()
    {
        while (_playback!.GetFramesAvailable() >= BlockFrames)
        {
            Generator!.Render(_mono);
            for (var i = 0; i < BlockFrames; i++)
            {
                _frames[i] = new Vector2(_mono[i], _mono[i]);
            }

            _playback.PushBuffer(_frames);
            if (Generator.IsFinished) return;
        }
    }
}
