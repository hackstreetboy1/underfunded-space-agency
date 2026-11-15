namespace Usa.Game.Presentation.Audio.Dsp;

/// <summary>A procedural sound streamed as mono float blocks at <see cref="AudioFormat.SampleRate"/>.</summary>
public interface ISoundGenerator
{
    SoundId Id { get; }

    bool IsLooping { get; }

    /// <summary>True once the sound (or a loop after <see cref="Stop"/>) has produced its last sample.</summary>
    bool IsFinished { get; }

    /// <summary>0..1. Loops react to it (level, brightness); one-shots use it as a gain.</summary>
    float Intensity { get; set; }

    /// <summary>Fills the whole buffer; writes silence after the sound has finished.</summary>
    void Render(Span<float> output);

    /// <summary>Starts the release: a short fade for one-shots, the sound's own fade-out for loops.</summary>
    void Stop();
}
