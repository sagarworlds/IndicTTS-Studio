using System.IO;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Text;

namespace IndicF5.Net;

/// <summary>
/// Core inference engine for IndicF5 TTS using ONNX Runtime in .NET 10.
/// 
/// Pipeline architecture:
///   1. F5_Preprocess.onnx  - Mel extraction, ConvNeXtV2 text embedding, RoPE tables, initial noise
///   2. F5_Transformer.onnx - DiT diffusion step (iterated 32 times)
///   3. F5_Decode.onnx      - Vocos neural vocoder + iSTFT reconstruction
/// </summary>
public class IndicF5Engine : IDisposable
{
    private readonly InferenceSession _preprocess;
    private readonly InferenceSession _transformer;
    private readonly InferenceSession _decode;
    private readonly TextTokenizer _tokenizer;

    private const int HopLength = 256;
    private const int DefaultSteps = 32;
    private const int MaxSignalLength = 4096;

    public int NumSteps { get; set; } = DefaultSteps;
    public int SampleRate { get; } = 24000;
    public float Speed { get; set; } = 1.0f;
    public bool IsLoaded { get; private set; }

    public IndicF5Engine(string modelDir, IProgress<GenerationProgress>? progress = null)
    {
        string preprocessPath = Path.Combine(modelDir, "F5_Preprocess.onnx");
        string transformerPath = Path.Combine(modelDir, "F5_Transformer.onnx");
        string decodePath = Path.Combine(modelDir, "F5_Decode.onnx");
        string vocabPath = Path.Combine(modelDir, "vocab.txt");

        foreach (var file in new[] { preprocessPath, transformerPath, decodePath, vocabPath })
        {
            if (!File.Exists(file))
                throw new FileNotFoundException($"Required model file not found: {file}");
        }

        var sessionOptions = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            InterOpNumThreads = Environment.ProcessorCount,
            IntraOpNumThreads = Environment.ProcessorCount,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            EnableCpuMemArena = true
        };

        progress?.Report(new GenerationProgress("Loading", 0, 3, 0, 0, "Loading F5_Preprocess.onnx..."));
        _preprocess = new InferenceSession(preprocessPath, sessionOptions);

        progress?.Report(new GenerationProgress("Loading", 1, 3, 0, 0, "Loading F5_Transformer.onnx..."));
        _transformer = new InferenceSession(transformerPath, sessionOptions);

        progress?.Report(new GenerationProgress("Loading", 2, 3, 0, 0, "Loading F5_Decode.onnx..."));
        _decode = new InferenceSession(decodePath, sessionOptions);

        _tokenizer = new TextTokenizer(vocabPath);
        progress?.Report(new GenerationProgress("Loading", 3, 3, 0, 0, $"Models loaded. Vocabulary: {_tokenizer.VocabSize} tokens."));

        IsLoaded = true;
    }

    /// <summary>
    /// Generate speech audio from text using voice cloning from a reference audio file.
    /// Automatically chunks long multi-sentence text into batches.
    /// </summary>
    public float[] Generate(
        string text,
        string refAudioPath,
        string refText,
        IProgress<GenerationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        float[] refAudio = AudioUtils.LoadWav(refAudioPath, SampleRate);
        float refDurationSec = refAudio.Length / (float)SampleRate;

        int refTextByteLen = Encoding.UTF8.GetByteCount(refText.Trim());
        int maxBytesPerChunk = (int)(refTextByteLen / Math.Max(refDurationSec, 1.0f) * 15.0f);
        maxBytesPerChunk = Math.Clamp(maxBytesPerChunk, 80, 400);

        var chunks = ChunkText(text, maxBytesPerChunk);
        int totalBatches = chunks.Count;

        progress?.Report(new GenerationProgress("Preprocessing", 0, NumSteps, 0, totalBatches,
            $"Splitting text into {totalBatches} batch(es)..."));

        var combinedAudio = new List<float>();

        for (int i = 0; i < chunks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(new GenerationProgress("Preprocessing", 0, NumSteps, i + 1, totalBatches,
                $"Batch {i + 1}/{totalBatches}: \"{Truncate(chunks[i], 60)}\""));

            var chunkAudio = GenerateSingleChunk(chunks[i], refAudio, refText, i + 1, totalBatches, progress, cancellationToken);
            combinedAudio.AddRange(chunkAudio);
        }

        progress?.Report(new GenerationProgress("Complete", NumSteps, NumSteps, totalBatches, totalBatches,
            $"Generation complete! Total samples: {combinedAudio.Count} ({combinedAudio.Count / (float)SampleRate:F2}s)"));

        return combinedAudio.ToArray();
    }

    /// <summary>
    /// Generate audio for a single sentence/text chunk.
    /// </summary>
    private float[] GenerateSingleChunk(
        string text,
        float[] refAudio,
        string refText,
        int batchIndex,
        int totalBatches,
        IProgress<GenerationProgress>? progress,
        CancellationToken cancellationToken)
    {
        int audioLen = refAudio.Length;
        int refAudioLen = audioLen / HopLength;

        // ── 1. Tokenize Text ────────────────────────────────────────────────
        string fullText = refText.Trim() + " " + text.Trim();
        int[] textIdsRaw = _tokenizer.Tokenize(fullText);

        var textIdsTensor = new DenseTensor<int>(textIdsRaw, new[] { 1, textIdsRaw.Length });

        int refTextByteLen = Encoding.UTF8.GetByteCount(refText.Trim());
        int genTextByteLen = Encoding.UTF8.GetByteCount(text.Trim());

        float speed = Speed;
        if (genTextByteLen < 10)
            speed = 0.3f;

        int duration = refAudioLen + (int)(refAudioLen / (float)Math.Max(refTextByteLen, 1) * genTextByteLen / speed);
        long maxDuration = Math.Min(Math.Max(Math.Max(textIdsRaw.Length, refAudioLen + 1) + 1, duration), MaxSignalLength);

        progress?.Report(new GenerationProgress("Preprocessing", 0, NumSteps, batchIndex, totalBatches,
            $"Batch {batchIndex}: {textIdsRaw.Length} tokens, {maxDuration} frames"));

        // ── 2. Run Preprocess ────────────────────────────────────────────────
        var audioTensor = new DenseTensor<float>(refAudio, new[] { 1, 1, audioLen });
        var durationTensor = new DenseTensor<long>(new[] { maxDuration }, new[] { 1 });

        var preprocessInputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("audio", audioTensor),
            NamedOnnxValue.CreateFromTensor("text_ids", textIdsTensor),
            NamedOnnxValue.CreateFromTensor("max_duration", durationTensor)
        };

        cancellationToken.ThrowIfCancellationRequested();

        using var preResults = _preprocess.Run(preprocessInputs);
        var preOutputs = preResults.ToDictionary(r => r.Name, r => r);

        var noiseTensor = preOutputs["noise"].Value as DenseTensor<float> ?? preOutputs["noise"].AsTensor<float>().ToDenseTensor();
        var ropeCosTensor = preOutputs["rope_cos"].Value as DenseTensor<float> ?? preOutputs["rope_cos"].AsTensor<float>().ToDenseTensor();
        var ropeSinTensor = preOutputs["rope_sin"].Value as DenseTensor<float> ?? preOutputs["rope_sin"].AsTensor<float>().ToDenseTensor();
        var catMelTextTensor = preOutputs["cat_mel_text"].Value as DenseTensor<float> ?? preOutputs["cat_mel_text"].AsTensor<float>().ToDenseTensor();
        var catMelTextDropTensor = preOutputs["cat_mel_text_drop"].Value as DenseTensor<float> ?? preOutputs["cat_mel_text_drop"].AsTensor<float>().ToDenseTensor();

        var refSignalLenVal = preOutputs["ref_signal_len"];
        var rmsScaleVal = preOutputs["rms_scale"];
        var refMelTailVal = preOutputs["ref_mel_tail"];

        // ── 3. Run Diffusion Transformer Loop ────────────────────────────────
        DenseTensor<float> currentState = noiseTensor;

        for (int step = 0; step < NumSteps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stepTensor = new DenseTensor<int>(new[] { step }, new[] { 1 });

            var transformerInputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("noise", currentState),
                NamedOnnxValue.CreateFromTensor("rope_cos", ropeCosTensor),
                NamedOnnxValue.CreateFromTensor("rope_sin", ropeSinTensor),
                NamedOnnxValue.CreateFromTensor("cat_mel_text", catMelTextTensor),
                NamedOnnxValue.CreateFromTensor("cat_mel_text_drop", catMelTextDropTensor),
                NamedOnnxValue.CreateFromTensor("time_step", stepTensor)
            };

            using var stepResults = _transformer.Run(transformerInputs);
            var denoised = stepResults.First().Value as DenseTensor<float> ?? stepResults.First().AsTensor<float>().ToDenseTensor();

            currentState = denoised;

            progress?.Report(new GenerationProgress("Diffusion", step, NumSteps, batchIndex, totalBatches,
                $"Batch {batchIndex}/{totalBatches} — Step {step + 1}/{NumSteps}"));
        }

        // ── 4. Run Vocos Decode ──────────────────────────────────────────────
        progress?.Report(new GenerationProgress("Decoding", NumSteps, NumSteps, batchIndex, totalBatches,
            $"Batch {batchIndex}: Decoding mel → waveform (Vocos + iSTFT)..."));

        cancellationToken.ThrowIfCancellationRequested();

        var decodeInputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("denoised", currentState),
            NamedOnnxValue.CreateFromTensor("ref_signal_len", refSignalLenVal.AsTensor<long>()),
            NamedOnnxValue.CreateFromTensor("rms_scale", rmsScaleVal.AsTensor<float>()),
            NamedOnnxValue.CreateFromTensor("ref_mel_tail", refMelTailVal.AsTensor<float>())
        };

        using var decResults = _decode.Run(decodeInputs);
        var audioOutput = decResults.First().AsTensor<float>().ToArray();

        progress?.Report(new GenerationProgress("Decoding", NumSteps, NumSteps, batchIndex, totalBatches,
            $"Batch {batchIndex}: Generated {audioOutput.Length / (float)SampleRate:F2}s of audio"));

        return audioOutput;
    }

    /// <summary>
    /// Split text into sentence/line batches to fit within model max signal length.
    /// </summary>
    private static List<string> ChunkText(string text, int maxBytes = 200)
    {
        var rawLines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                           .Select(l => l.Trim())
                           .Where(l => !string.IsNullOrEmpty(l))
                           .ToList();

        var chunks = new List<string>();
        var currentChunk = new StringBuilder();

        foreach (var line in rawLines)
        {
            int currentLen = Encoding.UTF8.GetByteCount(currentChunk.ToString());
            int lineLen = Encoding.UTF8.GetByteCount(line);

            if (currentChunk.Length > 0 && currentLen + lineLen > maxBytes)
            {
                chunks.Add(currentChunk.ToString());
                currentChunk.Clear();
            }

            if (currentChunk.Length > 0)
                currentChunk.Append(' ');
            currentChunk.Append(line);
        }

        if (currentChunk.Length > 0)
            chunks.Add(currentChunk.ToString());

        return chunks.Count > 0 ? chunks : new List<string> { text.Trim() };
    }

    private static string Truncate(string s, int maxLen)
        => s.Length <= maxLen ? s : s[..maxLen] + "…";

    public void Dispose()
    {
        _preprocess.Dispose();
        _transformer.Dispose();
        _decode.Dispose();
        GC.SuppressFinalize(this);
    }
}
