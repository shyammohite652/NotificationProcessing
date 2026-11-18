namespace NotificationProcessing.Infrastructure.Providers;

/// <summary>Bound from the "EmailProvider" section. Controls the simulation only.</summary>
public sealed class EmailProviderOptions
{
    public const string SectionName = "EmailProvider";

    /// <summary>Probability (0.0 – 1.0) that a send randomly fails. Use 0 for predictable demos.</summary>
    public double FailureRate { get; set; }

    /// <summary>Recipients in these domains always fail. Handy for demonstrating failure handling.</summary>
    public string[] AlwaysFailDomains { get; set; } = [];

    /// <summary>Fake network latency per send.</summary>
    public TimeSpan SimulatedLatency { get; set; } = TimeSpan.FromMilliseconds(50);
}
