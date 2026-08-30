namespace IndicF5.Net;

/// <summary>
/// Detailed progress information reported during speech generation.
/// </summary>
public record GenerationProgress(
    string Stage,        // "Loading", "Preprocessing", "Diffusion", "Decoding", "Complete"
    int CurrentStep,     // 0-based step index for diffusion (0–31)
    int TotalSteps,      // Total diffusion steps (e.g. 32)
    int CurrentBatch,    // 1-based batch index (1..TotalBatches)
    int TotalBatches,    // Total number of text chunks/batches
    string Message       // Human-readable status message
)
{
    /// <summary>
    /// Progress of the current batch (0–100%).
    /// </summary>
    public double BatchPercent
    {
        get
        {
            if (Stage == "Complete") return 100.0;
            if (TotalSteps <= 0) return 0.0;
            if (Stage == "Preprocessing") return 5.0;
            if (Stage == "Decoding") return 95.0;
            
            // Diffusion makes up 5% to 95% of the batch
            double diffusionFraction = (double)(CurrentStep + 1) / TotalSteps;
            return Math.Clamp(5.0 + (diffusionFraction * 90.0), 0.0, 100.0);
        }
    }

    /// <summary>
    /// Overall conversion progress across all batches (0–100%).
    /// </summary>
    public double OverallPercent
    {
        get
        {
            if (Stage == "Complete") return 100.0;
            if (TotalBatches <= 0) return 0.0;
            
            double completedBatchesFraction = Math.Max(0, CurrentBatch - 1);
            double currentBatchFraction = BatchPercent / 100.0;
            double totalProgress = (completedBatchesFraction + currentBatchFraction) / TotalBatches;
            
            return Math.Clamp(totalProgress * 100.0, 0.0, 100.0);
        }
    }
}
