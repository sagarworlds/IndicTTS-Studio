using System.IO;
using System.Text;

namespace IndicF5.Net;

/// <summary>
/// Tokenizer for Indic text used by the IndicF5/F5-TTS model.
/// The F5-TTS model uses a character-level vocabulary loaded from vocab.txt.
/// Each character (including Devanagari, spaces, punctuation) maps to an integer ID.
/// </summary>
public class TextTokenizer
{
    private readonly Dictionary<string, int> _charToId;
    private readonly Dictionary<int, string> _idToChar;

    // Special token IDs
    public int PadId { get; }
    public int BosId { get; }
    public int EosId { get; }

    /// <summary>
    /// Load the tokenizer from a vocab.txt file.
    /// The vocab file has one token per line, with the line number (0-indexed) as the ID.
    /// </summary>
    public TextTokenizer(string vocabPath)
    {
        _charToId = new Dictionary<string, int>();
        _idToChar = new Dictionary<int, string>();

        if (!File.Exists(vocabPath))
        {
            throw new FileNotFoundException($"Vocabulary file not found: {vocabPath}");
        }

        string[] lines = File.ReadAllLines(vocabPath, Encoding.UTF8);
        for (int i = 0; i < lines.Length; i++)
        {
            string token = lines[i].Trim();
            if (!string.IsNullOrEmpty(token))
            {
                _charToId[token] = i;
                _idToChar[i] = token;
            }
        }

        // Standard special tokens (adjust if vocab has different conventions)
        PadId = _charToId.GetValueOrDefault("[PAD]", 0);
        BosId = _charToId.GetValueOrDefault("[BOS]", 1);
        EosId = _charToId.GetValueOrDefault("[EOS]", 2);

        Console.WriteLine($"Loaded vocabulary with {_charToId.Count} tokens from {vocabPath}");
    }

    /// <summary>
    /// Tokenize text to integer IDs using character-level encoding.
    /// Each character in the text is individually mapped to its vocab ID.
    /// Unknown characters are skipped with a warning.
    /// </summary>
    /// <summary>
    /// Tokenize text to integer IDs using character-level encoding.
    /// Normalizes punctuation and whitespace, then maps each character to its vocab ID.
    /// </summary>
    public int[] Tokenize(string text)
    {
        // Normalize punctuation and whitespace
        string cleaned = text
            .Replace(';', ',')
            .Replace('“', '"')
            .Replace('”', '"')
            .Replace('‘', '\'')
            .Replace('’', '\'')
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ');

        // Collapse multiple spaces
        while (cleaned.Contains("  "))
            cleaned = cleaned.Replace("  ", " ");

        var ids = new List<int>();

        foreach (char c in cleaned)
        {
            string charStr = c.ToString();

            if (charStr == " ")
            {
                if (_charToId.TryGetValue(" ", out int spaceId))
                    ids.Add(spaceId);
                else
                    ids.Add(0);
            }
            else if (_charToId.TryGetValue(charStr, out int id))
            {
                ids.Add(id);
            }
            else
            {
                // Unknown character - fallback to space token (index 0)
                ids.Add(0);
            }
        }

        return ids.ToArray();
    }

    /// <summary>
    /// Convert token IDs back to text.
    /// </summary>
    public string Detokenize(int[] ids)
    {
        var sb = new StringBuilder();
        foreach (int id in ids)
        {
            if (_idToChar.TryGetValue(id, out string? token))
            {
                if (token != "[PAD]" && token != "[BOS]" && token != "[EOS]")
                    sb.Append(token);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Get the vocabulary size.
    /// </summary>
    public int VocabSize => _charToId.Count;
}
