using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace IndicF5.Net;

/// <summary>
/// Audio I/O utilities for loading, resampling, and converting WAV files.
/// IndicF5 expects 24 kHz mono float32 audio.
/// </summary>
public static class AudioUtils
{
    public const int TargetSampleRate = 24000;
    public const int TargetChannels = 1;

    /// <summary>
    /// Load a WAV file and return mono float32 samples at the target sample rate.
    /// Handles resampling and channel conversion automatically.
    /// </summary>
    public static float[] LoadWav(string path, int targetSampleRate = TargetSampleRate)
    {
        using var reader = new AudioFileReader(path);

        ISampleProvider source = reader;

        // Convert to mono if stereo
        if (reader.WaveFormat.Channels > 1)
        {
            source = new StereoToMonoSampleProvider(source)
            {
                LeftVolume = 0.5f,
                RightVolume = 0.5f
            };
        }

        // Resample if needed
        if (reader.WaveFormat.SampleRate != targetSampleRate)
        {
            var resampler = new WdlResamplingSampleProvider(source, targetSampleRate);
            source = resampler;
        }

        // Read all samples
        var samples = new List<float>();
        var buffer = new float[4096];
        int read;
        while ((read = source.Read(buffer.AsSpan())) > 0)
        {
            for (int i = 0; i < read; i++)
            {
                samples.Add(buffer[i]);
            }
        }

        return samples.ToArray();
    }

    /// <summary>
    /// Normalize float audio to [-1.0, 1.0] range.
    /// </summary>
    public static float[] Normalize(float[] audio)
    {
        if (audio.Length == 0)
            return audio;

        float maxAbs = 0f;
        for (int i = 0; i < audio.Length; i++)
        {
            float abs = MathF.Abs(audio[i]);
            if (abs > maxAbs)
                maxAbs = abs;
        }

        if (maxAbs < 1e-8f)
            return audio;

        var normalized = new float[audio.Length];
        for (int i = 0; i < audio.Length; i++)
        {
            normalized[i] = audio[i] / maxAbs;
        }

        return normalized;
    }

    /// <summary>
    /// Convert float32 samples to int16 samples.
    /// Mirrors the Python: (audio * 32768).astype(np.int16)
    /// </summary>
    public static short[] FloatToInt16(float[] audio)
    {
        var result = new short[audio.Length];
        for (int i = 0; i < audio.Length; i++)
        {
            float sample = Math.Clamp(audio[i], -1.0f, 1.0f);
            result[i] = (short)(sample * 32767f);
        }
        return result;
    }

    /// <summary>
    /// Convert int16 samples to float32 samples.
    /// Mirrors the Python: audio.astype(np.float32) / 32768.0
    /// </summary>
    public static float[] Int16ToFloat(short[] audio)
    {
        var result = new float[audio.Length];
        for (int i = 0; i < audio.Length; i++)
        {
            result[i] = audio[i] / 32768.0f;
        }
        return result;
    }
}
