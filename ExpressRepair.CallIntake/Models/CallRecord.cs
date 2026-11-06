namespace ExpressRepair.CallIntake.Models;

/// <summary>
/// One inbound voicemail, from the moment the webhook lands to the moment
/// the AI pipeline finishes with it. This is the only table in the app.
/// </summary>
public class CallRecord
{
    public int Id { get; set; }

    // --- Facts from the telephony provider (never changed after insert) ---

    /// <summary>Provider's unique id for the call. Our idempotency key.</summary>
    public string CallSid { get; set; } = string.Empty;

    /// <summary>Caller's number, E.164.</summary>
    public string From { get; set; } = string.Empty;

    /// <summary>The number they dialled, E.164.</summary>
    public string To { get; set; } = string.Empty;

    public string RecordingSid { get; set; } = string.Empty;
    public string RecordingUrl { get; set; } = string.Empty;
    public int RecordingDuration { get; set; }

    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;

    // --- Pipeline state (written by the background worker) ---

    public CallStatus Status { get; set; } = CallStatus.Processing;
    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>How many times the worker has tried this call. Used by the retry rule.</summary>
    public int Attempts { get; set; }

    /// <summary>Why it failed, shown in the UI. Null unless Status is Failed.</summary>
    public string? Error { get; set; }

    // --- Results ---

    public string? Transcript { get; set; }

    public string? CustomerName { get; set; }
    public string? Appliance { get; set; }
    public string? Brand { get; set; }
    public string? Problem { get; set; }
    public string? Urgency { get; set; }
    public string? PreferredTime { get; set; }
}
