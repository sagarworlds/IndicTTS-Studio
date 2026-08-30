# IndicF5.Net

A modern **.NET 10 (Windows / WPF)** desktop application for **Indic Text-to-Speech (TTS)** voice cloning powered by **ONNX Runtime** and **NAudio**.

Based on the [ai4bharat/IndicF5](https://huggingface.co/ai4bharat/IndicF5) and [F5-TTS](https://github.com/SWivid/F5-TTS) Flow Matching Diffusion architecture.

---

## ✨ Features

- **🎙️ Zero-Shot Voice Cloning**: Clone any voice sample using a short reference audio WAV recording.
- **🇮🇳 Multi-Language Indic Support**: Synthesizes Devanagari, Marathi, Hindi, and other Indian languages.
- **⚡ Local Offline Inference**: 100% offline speech synthesis using ONNX Runtime.
- **🖥️ WPF Desktop UI**:
  - Multiline text editor with Unicode font support (`Nirmala UI`, `Segoe UI`).
  - Reference audio file picker with validation & audio player (`▶ Play Ref`).
  - Dynamic speed control (`0.5x` – `2.0x`) and diffusion steps selector (`8`, `16`, `32`).
  - Live progress bar, batch & step indicators, and ETA calculations.
  - Built-in audio playback controls with seeking slider and WAV export (`💾 Save WAV...`).
  - Session Reset button (`🔄 Clear & Reset`).

---

## 🏗️ Architecture

```
IndicF5.Net/
├── App.xaml / App.xaml.cs       # WPF application startup & theme resources
├── MainWindow.xaml / .xaml.cs   # Desktop GUI & audio player logic
├── IndicF5Engine.cs            # 3-Stage ONNX pipeline + multi-batch chunking
├── TextTokenizer.cs            # Indic character-level tokenizer (2,540 tokens)
├── GenerationProgress.cs       # Progress reporting data structure
├── AudioUtils.cs               # NAudio 24kHz mono audio loader & resampler
├── WavWriter.cs                # RIFF WAV writer
└── Models/                     # Place exported ONNX models here
    ├── F5_Preprocess.onnx
    ├── F5_Transformer.onnx
    ├── F5_Decode.onnx
    ├── F5_Metadata.onnx
    └── vocab.txt
```

---

## 🚀 Getting Started

### 1. Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (or Windows 10/11 with .NET Desktop Runtime)
- ONNX model files placed in the `Models/` folder

### 2. Run the Application

```powershell
dotnet run
```

Or open `IndicF5.Net.csproj` in Visual Studio 2022+ / JetBrains Rider.
