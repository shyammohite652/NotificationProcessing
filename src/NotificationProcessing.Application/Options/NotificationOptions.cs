namespace NotificationProcessing.Application.Options;

/// <summary>Bound from the "Notifications" configuration section.</summary>
public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    /// <summary>
    /// Identical requests received within this window are treated as client retries and are not stored again.
    /// </summary>
    public TimeSpan DuplicateDetectionWindow { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Maximum number of notifications one call to the process endpoint will pick up.</summary>
    public int ProcessingBatchSize { get; set; } = 50;
}
