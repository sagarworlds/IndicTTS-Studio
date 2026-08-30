using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace IndicF5.Net;

public partial class MainWindow : Window
{
    private IndicF5Engine? _engine;
    private CancellationTokenSource? _cts;
    private float[]? _generatedAudio;
    private string? _generatedWavPath;

    // Generated audio playback
    private WaveOut? _waveOut;
    private AudioFileReader? _genAudioReader;
    private DispatcherTimer? _playbackTimer;
    private bool _isUserDragging;

    // Reference audio playback & validation
    private WaveOut? _refWaveOut;
    private AudioFileReader? _refAudioReader;
    private bool _isRefAudioValid;

    public MainWindow()
    {
        InitializeComponent();
        SliderSpeed.ValueChanged += (_, _) =>
            TxtSpeedValue.Text = $"{SliderSpeed.Value:F1}x";

        SliderPosition.PreviewMouseDown += (_, _) => _isUserDragging = true;
        SliderPosition.PreviewMouseUp += (_, _) =>
        {
            _isUserDragging = false;
            if (_genAudioReader != null)
            {
                _genAudioReader.CurrentTime = TimeSpan.FromSeconds(SliderPosition.Value);
                TxtPlayPosition.Text = FormatTime(SliderPosition.Value);
            }
        };

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Set default reference audio path
        string defaultRef = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "my_voice.wav");
        if (!File.Exists(defaultRef))
            defaultRef = Path.Combine(Directory.GetCurrentDirectory(), "my_voice.wav");
        if (!File.Exists(defaultRef))
            defaultRef = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "IndicF5", "my_voice.wav"));

        if (File.Exists(defaultRef))
        {
            TxtRefAudio.Text = Path.GetFullPath(defaultRef);
        }
        else
        {
            ValidateReferenceAudio(TxtRefAudio.Text.Trim());
        }

        // Auto-load models
        await LoadModelsAsync();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        StopGeneratedPlayback();
        StopReferencePlayback();
        _engine?.Dispose();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  REFERENCE AUDIO VALIDATION & PLAYBACK
    // ════════════════════════════════════════════════════════════════════════

    private void TxtRefAudio_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        ValidateReferenceAudio(TxtRefAudio.Text.Trim());
    }

    private void ValidateReferenceAudio(string path)
    {
        StopReferencePlayback();

        if (string.IsNullOrWhiteSpace(path))
        {
            SetRefAudioState(false, "⚠ Please select a reference WAV audio file.", Colors.Orange);
            return;
        }

        string fullPath = Path.IsPathRooted(path) ? path : Path.Combine(Directory.GetCurrentDirectory(), path);

        if (!File.Exists(fullPath))
        {
            SetRefAudioState(false, "✗ File not found. Please browse for a valid WAV file.", Color.FromRgb(255, 107, 107));
            return;
        }

        try
        {
            using var reader = new AudioFileReader(fullPath);
            double durationSec = reader.TotalTime.TotalSeconds;
            int sampleRate = reader.WaveFormat.SampleRate;
            int channels = reader.WaveFormat.Channels;

            string channelStr = channels == 1 ? "Mono" : $"{channels}ch";
            string msg = $"✓ Valid Audio: {durationSec:F2}s ({sampleRate} Hz, {channelStr})";

            SetRefAudioState(true, msg, Color.FromRgb(107, 203, 119));
        }
        catch (Exception ex)
        {
            SetRefAudioState(false, $"✗ Invalid audio file: {ex.Message}", Color.FromRgb(255, 107, 107));
        }
    }

    private void SetRefAudioState(bool isValid, string message, Color color)
    {
        _isRefAudioValid = isValid;
        if (BtnPlayRefAudio != null)
        {
            BtnPlayRefAudio.IsEnabled = isValid;
            BtnPlayRefAudio.Content = "▶ Play Ref";
        }
        if (TxtRefAudioValidation != null)
        {
            TxtRefAudioValidation.Text = message;
            TxtRefAudioValidation.Foreground = new SolidColorBrush(color);
        }
    }

    private void BtnPlayRefAudio_Click(object sender, RoutedEventArgs e)
    {
        if (!_isRefAudioValid) return;

        string path = TxtRefAudio.Text.Trim();
        string fullPath = Path.IsPathRooted(path) ? path : Path.Combine(Directory.GetCurrentDirectory(), path);

        if (!File.Exists(fullPath))
        {
            ValidateReferenceAudio(path);
            return;
        }

        // Toggle playback
        if (_refWaveOut?.PlaybackState == PlaybackState.Playing)
        {
            _refWaveOut.Pause();
            BtnPlayRefAudio.Content = "▶ Play Ref";
            return;
        }

        if (_refWaveOut?.PlaybackState == PlaybackState.Paused)
        {
            _refWaveOut.Play();
            BtnPlayRefAudio.Content = "⏸ Pause Ref";
            return;
        }

        // Stop generated audio playback if playing
        StopGeneratedPlayback();
        StopReferencePlayback();

        try
        {
            // AudioFileReader supports any bit-depth (16-bit, 24-bit, 32-bit float, WAVEX, etc.)
            // SampleToWaveProvider16 converts the stream to standard 16-bit PCM for any sound card
            _refAudioReader = new AudioFileReader(fullPath);
            var waveProvider = new SampleToWaveProvider16(_refAudioReader);

            _refWaveOut = new WaveOut();
            _refWaveOut.Init(waveProvider);
            _refWaveOut.PlaybackStopped += (_, _) => Dispatcher.Invoke(() =>
            {
                BtnPlayRefAudio.Content = "▶ Play Ref";
                StopReferencePlayback();
            });

            _refWaveOut.Play();
            BtnPlayRefAudio.Content = "⏸ Pause Ref";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not play reference audio:\n{ex.Message}", "Playback Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            StopReferencePlayback();
        }
    }

    private void StopReferencePlayback()
    {
        if (_refWaveOut != null)
        {
            _refWaveOut.Stop();
            _refWaveOut.Dispose();
            _refWaveOut = null;
        }
        if (_refAudioReader != null)
        {
            _refAudioReader.Dispose();
            _refAudioReader = null;
        }
        if (BtnPlayRefAudio != null)
        {
            BtnPlayRefAudio.Content = "▶ Play Ref";
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  MODEL LOADING
    // ════════════════════════════════════════════════════════════════════════

    private async Task LoadModelsAsync()
    {
        string modelDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models");
        if (!Directory.Exists(modelDir))
            modelDir = Path.Combine(Directory.GetCurrentDirectory(), "Models");

        if (!Directory.Exists(modelDir))
        {
            TxtStatus.Text = "⚠ Models directory not found. Place ONNX models in the 'Models' folder.";
            AppendLog("ERROR: Models directory not found.");
            BtnGenerate.IsEnabled = false;
            return;
        }

        BtnGenerate.IsEnabled = false;
        TxtStatus.Text = "Loading ONNX models (this may take a moment)...";
        SetStageBadge("LOADING", Color.FromRgb(157, 107, 255));

        var progress = new Progress<GenerationProgress>(p =>
        {
            AppendLog(p.Message);
            TxtStatus.Text = p.Message;
        });

        try
        {
            _engine = await Task.Run(() => new IndicF5Engine(modelDir, progress));
            TxtStatus.Text = "✓ Models loaded — ready to generate speech.";
            SetStageBadge("READY", Color.FromRgb(107, 203, 119));
            BtnGenerate.IsEnabled = true;
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR loading models: {ex.Message}");
            TxtStatus.Text = $"✗ Failed to load models: {ex.Message}";
            SetStageBadge("ERROR", Color.FromRgb(255, 107, 107));
            BtnGenerate.IsEnabled = false;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  GENERATION WITH DETAILED OVERALL PROGRESS
    // ════════════════════════════════════════════════════════════════════════

    private async void BtnGenerate_Click(object sender, RoutedEventArgs e)
    {
        if (_engine == null) return;

        string text = TxtInput.Text.Trim();
        string refAudioPath = TxtRefAudio.Text.Trim();
        string refText = TxtRefText.Text.Trim();

        if (string.IsNullOrEmpty(text))
        {
            MessageBox.Show("Please enter text to synthesize.", "Input Required",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!_isRefAudioValid || !File.Exists(refAudioPath))
        {
            MessageBox.Show("Please select a valid reference audio file (WAV format).",
                "Reference Audio Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrEmpty(refText))
        {
            MessageBox.Show("Please enter reference text (the exact transcript spoken in the reference audio).",
                "Reference Text Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Configure engine
        _engine.Speed = (float)SliderSpeed.Value;
        var selectedSteps = (CmbSteps.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString();
        _engine.NumSteps = int.TryParse(selectedSteps, out int steps) ? steps : 32;

        // UI state: generating
        StopGeneratedPlayback();
        StopReferencePlayback();
        _generatedAudio = null;
        SetGeneratingUI(true);
        ClearLog();

        ProgressBarOverall.Value = 0;
        TxtOverallPercent.Text = "0%";
        TxtBatchStepInfo.Text = "Initializing speech generation pipeline...";
        TxtTimeMetrics.Text = "Elapsed: 00:00";
        SetStageBadge("STARTING", Color.FromRgb(157, 107, 255));

        _cts = new CancellationTokenSource();
        var sw = Stopwatch.StartNew();

        var progress = new Progress<GenerationProgress>(p =>
        {
            double overallPct = p.OverallPercent;
            ProgressBarOverall.Value = overallPct;
            TxtOverallPercent.Text = $"{overallPct:F0}%";

            // Elapsed and ETA calculation
            double elapsedSec = sw.Elapsed.TotalSeconds;
            string elapsedStr = FormatTime(elapsedSec);
            string timeInfo = $"Elapsed: {elapsedStr}";

            if (overallPct > 5.0 && overallPct < 99.0)
            {
                double totalEstimatedSec = elapsedSec / (overallPct / 100.0);
                double remainingSec = Math.Max(0, totalEstimatedSec - elapsedSec);
                timeInfo = $"Elapsed: {elapsedStr} • ETA: ~{FormatTime(remainingSec)}";
            }

            TxtTimeMetrics.Text = timeInfo;

            // Stage badge & batch details
            switch (p.Stage)
            {
                case "Preprocessing":
                    SetStageBadge("PREPROCESS", Color.FromRgb(255, 179, 71));
                    TxtBatchStepInfo.Text = $"Batch {p.CurrentBatch} of {p.TotalBatches} • Preprocessing audio & text";
                    break;

                case "Diffusion":
                    SetStageBadge($"DIFFUSION {p.CurrentStep + 1}/{p.TotalSteps}", Color.FromRgb(157, 107, 255));
                    TxtBatchStepInfo.Text = $"Batch {p.CurrentBatch} of {p.TotalBatches} • Step {p.CurrentStep + 1} of {p.TotalSteps} ({p.BatchPercent:F0}% of batch)";
                    break;

                case "Decoding":
                    SetStageBadge("DECODING", Color.FromRgb(77, 208, 225));
                    TxtBatchStepInfo.Text = $"Batch {p.CurrentBatch} of {p.TotalBatches} • Vocos neural vocoder decoding";
                    break;

                case "Complete":
                    SetStageBadge("COMPLETE", Color.FromRgb(107, 203, 119));
                    TxtBatchStepInfo.Text = $"Conversion complete ({p.TotalBatches} batches processed)";
                    break;

                default:
                    SetStageBadge(p.Stage.ToUpper(), Color.FromRgb(157, 107, 255));
                    TxtBatchStepInfo.Text = p.Message;
                    break;
            }

            TxtStatus.Text = $"Generating speech... ({overallPct:F0}% overall • {elapsedStr} elapsed)";
            AppendLog(p.Message);
        });

        try
        {
            float[] audio = await Task.Run(() =>
                _engine.Generate(text, refAudioPath, refText, progress, _cts.Token));

            sw.Stop();

            // Normalize
            if (audio.Length > 0)
            {
                float maxAbs = audio.Max(x => MathF.Abs(x));
                if (maxAbs > 1.0f)
                    for (int i = 0; i < audio.Length; i++)
                        audio[i] /= maxAbs;
            }

            _generatedAudio = audio;

            // Save temporary WAV
            string tempPath = Path.Combine(Path.GetTempPath(), "indicf5_output.wav");
            WavWriter.Save(tempPath, audio, sampleRate: 24000);
            _generatedWavPath = tempPath;

            ProgressBarOverall.Value = 100;
            TxtOverallPercent.Text = "100%";
            SetStageBadge("COMPLETE", Color.FromRgb(107, 203, 119));
            double durationSec = audio.Length / 24000.0;
            TxtBatchStepInfo.Text = $"✓ Synthesized {durationSec:F2}s of audio in {sw.Elapsed.TotalSeconds:F1}s";
            TxtTimeMetrics.Text = $"Total Time: {FormatTime(sw.Elapsed.TotalSeconds)}";
            TxtStatus.Text = $"✓ Done! {durationSec:F2}s audio generated in {sw.Elapsed.TotalSeconds:F1}s";
            AppendLog($"✓ Generation complete: {durationSec:F2}s audio in {sw.Elapsed.TotalSeconds:F1}s");

            // Enable playback
            BtnPlay.IsEnabled = true;
            BtnStop.IsEnabled = true;
            BtnSaveAs.IsEnabled = true;
            SliderPosition.IsEnabled = true;
            TxtPlayDuration.Text = FormatTime(durationSec);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            SetStageBadge("CANCELLED", Color.FromRgb(255, 107, 107));
            AppendLog("⚠ Generation cancelled by user.");
            TxtStatus.Text = "Generation cancelled.";
            TxtBatchStepInfo.Text = "Synthesis aborted by user.";
        }
        catch (Exception ex)
        {
            sw.Stop();
            SetStageBadge("ERROR", Color.FromRgb(255, 107, 107));
            AppendLog($"ERROR: {ex.Message}");
            TxtStatus.Text = $"✗ Error: {ex.Message}";
            TxtBatchStepInfo.Text = $"Error: {ex.Message}";
            MessageBox.Show($"Generation failed:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetGeneratingUI(false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        // Stop any active audio playback
        StopGeneratedPlayback();
        StopReferencePlayback();

        // Clear input text
        TxtInput.Text = "";

        // Reset progress indicators
        ProgressBarOverall.Value = 0;
        TxtOverallPercent.Text = "0%";
        TxtBatchStepInfo.Text = "Ready to synthesize.";
        TxtTimeMetrics.Text = "";
        SetStageBadge("READY", Color.FromRgb(107, 203, 119));

        // Reset generated audio state
        _generatedAudio = null;
        _generatedWavPath = null;
        BtnPlay.IsEnabled = false;
        BtnStop.IsEnabled = false;
        BtnSaveAs.IsEnabled = false;
        SliderPosition.Value = 0;
        SliderPosition.IsEnabled = false;
        TxtPlayPosition.Text = "0:00";
        TxtPlayDuration.Text = "0:00";

        // Clear logs and show reset notification
        ClearLog();
        AppendLog("Session cleared and reset. Ready for new input.");

        TxtStatus.Text = "Ready — Enter text to begin.";
        TxtInput.Focus();
    }

    private void BtnClearText_Click(object sender, RoutedEventArgs e)
    {
        TxtInput.Text = "";
        TxtInput.Focus();
    }

    private void SetGeneratingUI(bool generating)
    {
        BtnGenerate.IsEnabled = !generating;
        BtnCancel.Visibility = generating ? Visibility.Visible : Visibility.Collapsed;
        TxtInput.IsEnabled = !generating;
        TxtRefAudio.IsEnabled = !generating;
        TxtRefText.IsEnabled = !generating;
        SliderSpeed.IsEnabled = !generating;
        CmbSteps.IsEnabled = !generating;
    }

    private void SetStageBadge(string text, Color color)
    {
        if (TxtStageBadge != null)
        {
            TxtStageBadge.Text = text;
            TxtStageBadge.Foreground = new SolidColorBrush(color);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  GENERATED AUDIO PLAYBACK
    // ════════════════════════════════════════════════════════════════════════

    private void BtnPlay_Click(object sender, RoutedEventArgs e)
    {
        if (_generatedWavPath == null || !File.Exists(_generatedWavPath)) return;

        if (_waveOut?.PlaybackState == PlaybackState.Playing)
        {
            _waveOut.Pause();
            BtnPlay.Content = "▶";
            return;
        }

        if (_waveOut?.PlaybackState == PlaybackState.Paused)
        {
            _waveOut.Play();
            BtnPlay.Content = "⏸";
            return;
        }

        // Start fresh playback
        StopReferencePlayback();
        StopGeneratedPlayback();

        try
        {
            _genAudioReader = new AudioFileReader(_generatedWavPath);
            var waveProvider = new SampleToWaveProvider16(_genAudioReader);

            _waveOut = new WaveOut();
            _waveOut.Init(waveProvider);
            _waveOut.PlaybackStopped += (_, _) => Dispatcher.Invoke(() =>
            {
                BtnPlay.Content = "▶";
                _playbackTimer?.Stop();
            });

            SliderPosition.Maximum = _genAudioReader.TotalTime.TotalSeconds;
            _waveOut.Play();
            BtnPlay.Content = "⏸";

            // Playback position timer
            _playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _playbackTimer.Tick += (_, _) =>
            {
                if (_genAudioReader != null && !_isUserDragging)
                {
                    SliderPosition.Value = _genAudioReader.CurrentTime.TotalSeconds;
                    TxtPlayPosition.Text = FormatTime(_genAudioReader.CurrentTime.TotalSeconds);
                }
            };
            _playbackTimer.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not play generated audio:\n{ex.Message}", "Playback Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            StopGeneratedPlayback();
        }
    }

    private void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        StopGeneratedPlayback();
        SliderPosition.Value = 0;
        TxtPlayPosition.Text = "0:00";
    }

    private void StopGeneratedPlayback()
    {
        _playbackTimer?.Stop();
        _playbackTimer = null;

        if (_waveOut != null)
        {
            _waveOut.Stop();
            _waveOut.Dispose();
            _waveOut = null;
        }
        if (_genAudioReader != null)
        {
            _genAudioReader.Dispose();
            _genAudioReader = null;
        }
        if (BtnPlay != null)
        {
            BtnPlay.Content = "▶";
        }
    }

    private void SliderPosition_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_genAudioReader != null && _isUserDragging)
        {
            _genAudioReader.CurrentTime = TimeSpan.FromSeconds(SliderPosition.Value);
            TxtPlayPosition.Text = FormatTime(SliderPosition.Value);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  FILE BROWSE & SAVE
    // ════════════════════════════════════════════════════════════════════════

    private void BtnBrowseAudio_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "WAV Audio (*.wav)|*.wav|All Audio Files (*.wav;*.mp3)|*.wav;*.mp3|All Files (*.*)|*.*",
            Title = "Select Reference Audio Recording"
        };
        if (dlg.ShowDialog() == true)
        {
            TxtRefAudio.Text = dlg.FileName;
            ValidateReferenceAudio(dlg.FileName);
        }
    }

    private void BtnSaveAs_Click(object sender, RoutedEventArgs e)
    {
        if (_generatedAudio == null) return;

        var dlg = new SaveFileDialog
        {
            Filter = "WAV Audio (*.wav)|*.wav",
            DefaultExt = ".wav",
            FileName = "indicf5_output.wav",
            Title = "Save Synthesized Speech As WAV"
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                WavWriter.Save(dlg.FileName, _generatedAudio, sampleRate: 24000);
                AppendLog($"Saved output: {dlg.FileName}");
                TxtStatus.Text = $"✓ Saved to {dlg.FileName}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save audio file:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ════════════════════════════════════════════════════════════════════════

    private void AppendLog(string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        TxtLog.Text += $"[{timestamp}] {message}\n";
        LogScroller.ScrollToEnd();
    }

    private void ClearLog()
    {
        TxtLog.Text = "";
    }

    private static string FormatTime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(seconds, 0));
        return ts.TotalMinutes >= 1
            ? $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}"
            : $"0:{ts.Seconds:D2}";
    }
}
