namespace ExpressRepair.CallIntake.Models;

/// <summary>
/// The payload the telephony provider sends when a recording is ready.
/// Property names match the provider's PascalCase field names exactly.
/// </summary>
public sealed class RecordingWebhookRequest
{
    public string? CallSid { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public string? RecordingSid { get; set; }
    public string? RecordingUrl { get; set; }

    /// <summary>
    /// Arrives as a string ("47") in both form and JSON payloads, so it is
    /// typed as string here and parsed once, defensively, at the edge.
    /// </summary>
    public string? RecordingDuration { get; set; }

    /// <summary>True when we have the minimum needed to create a call row.</summary>
    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(CallSid) &&
        !string.IsNullOrWhiteSpace(From) &&
        !string.IsNullOrWhiteSpace(RecordingUrl);
}
