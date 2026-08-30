using System.IO;

namespace IndicF5.Net;

/// <summary>
/// Simple WAV file writer for saving generated audio.
/// Replaces Python's soundfile.write().
/// </summary>
public static class WavWriter
{
    /// <summary>
    /// Save float32 audio samples to a WAV file.
    /// </summary>
    /// <param name="path">Output file path.</param>
    /// <param name="audio">Float32 audio samples (range [-1.0, 1.0]).</param>
    /// <param name="sampleRate">Sample rate in Hz (default 24000 for IndicF5).</param>
    public static void Save(string path, float[] audio, int sampleRate = 24000)
    {
        // WAV file format: RIFF header + PCM 16-bit data
        short[] int16Audio = AudioUtils.FloatToInt16(audio);

        using var writer = new BinaryWriter(File.Create(path));

        int dataSize = int16Audio.Length * 2; // 2 bytes per int16 sample
        int channels = 1;
        int bitsPerSample = 16;
        int byteRate = sampleRate * channels * bitsPerSample / 8;
        int blockAlign = channels * bitsPerSample / 8;

        // RIFF header
        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);        // File size - 8
        writer.Write("WAVE"u8);

        // fmt subchunk
        writer.Write("fmt "u8);
        writer.Write(16);                    // Subchunk size (PCM = 16)
        writer.Write((short)1);              // Audio format (PCM = 1)
        writer.Write((short)channels);       // Number of channels
        writer.Write(sampleRate);            // Sample rate
        writer.Write(byteRate);              // Byte rate
        writer.Write((short)blockAlign);     // Block align
        writer.Write((short)bitsPerSample);  // Bits per sample

        // data subchunk
        writer.Write("data"u8);
        writer.Write(dataSize);

        // Write audio data
        for (int i = 0; i < int16Audio.Length; i++)
        {
            writer.Write(int16Audio[i]);
        }
    }

    /// <summary>
    /// Save float32 audio samples directly as 32-bit float WAV.
    /// </summary>
    public static void SaveFloat32(string path, float[] audio, int sampleRate = 24000)
    {
        using var writer = new BinaryWriter(File.Create(path));

        int dataSize = audio.Length * 4; // 4 bytes per float32
        int channels = 1;
        int bitsPerSample = 32;
        int byteRate = sampleRate * channels * bitsPerSample / 8;
        int blockAlign = channels * bitsPerSample / 8;

        // RIFF header
        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8);

        // fmt subchunk
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)3);              // Audio format (IEEE float = 3)
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write((short)blockAlign);
        writer.Write((short)bitsPerSample);

        // data subchunk
        writer.Write("data"u8);
        writer.Write(dataSize);

        for (int i = 0; i < audio.Length; i++)
        {
            writer.Write(audio[i]);
        }
    }
}
