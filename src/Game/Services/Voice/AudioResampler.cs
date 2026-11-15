namespace Usa.Game.Services.Voice;

/// <summary>
/// Offline sample-rate conversion with a Hann-windowed sinc kernel. When downsampling the cutoff drops to
/// 95 % of the target Nyquist so speech energy above 8 kHz (at 16 kHz) does not alias into the transcript band.
/// Weights are renormalised per output sample, so DC gain is exactly 1 including at the edges. Pure.
/// </summary>
public static class AudioResampler
{
    private const int ZeroCrossings = 8;
    private const double CutoffMargin = 0.95;

    // The kernel is tabulated at 1/256 input sample: 15 s at 48 kHz then costs ~12 M multiply-adds, no trig.
    private const int TableStepsPerSample = 256;

    public static float[] Resample(ReadOnlySpan<float> input, int sourceRateHz, int targetRateHz)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceRateHz, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(targetRateHz, 1);
        if (input.IsEmpty) return [];
        if (sourceRateHz == targetRateHz) return input.ToArray();

        var step = (double)sourceRateHz / targetRateHz;
        var outputLength = (int)Math.Floor(input.Length / step);
        var output = new float[outputLength];

        // Cutoff in cycles per input sample; 0.5 is the source Nyquist.
        var cutoff = 0.5 * Math.Min(1.0, (double)targetRateHz / sourceRateHz) * CutoffMargin;
        var halfWidth = ZeroCrossings / (2.0 * cutoff);
        var kernel = BuildKernel(cutoff, halfWidth);

        for (var n = 0; n < outputLength; n++)
        {
            var center = n * step;
            var first = Math.Max(0, (int)Math.Ceiling(center - halfWidth));
            var last = Math.Min(input.Length - 1, (int)Math.Floor(center + halfWidth));
            double sum = 0, weightSum = 0;
            for (var k = first; k <= last; k++)
            {
                var index = (int)(Math.Abs(center - k) * TableStepsPerSample);
                if (index >= kernel.Length) continue;
                var weight = kernel[index];
                sum += input[k] * weight;
                weightSum += weight;
            }
            output[n] = weightSum > 1e-9 ? (float)(sum / weightSum) : 0f;
        }
        return output;
    }

    private static double[] BuildKernel(double cutoff, double halfWidth)
    {
        var kernel = new double[(int)Math.Ceiling(halfWidth * TableStepsPerSample) + 1];
        for (var i = 0; i < kernel.Length; i++)
        {
            var distance = (double)i / TableStepsPerSample;
            kernel[i] = Sinc(2.0 * cutoff * distance) * Hann(distance / halfWidth);
        }
        return kernel;
    }

    private static double Sinc(double x) => Math.Abs(x) < 1e-9 ? 1.0 : Math.Sin(Math.PI * x) / (Math.PI * x);

    private static double Hann(double normalized) =>
        Math.Abs(normalized) >= 1.0 ? 0.0 : 0.5 * (1.0 + Math.Cos(Math.PI * normalized));
}
