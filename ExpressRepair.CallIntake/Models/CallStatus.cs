namespace ExpressRepair.CallIntake.Models;

public enum CallStatus
{
    /// <summary>Webhook accepted, queued, not yet processed.</summary>
    Processing = 0,

    /// <summary>Transcribed and extracted successfully.</summary>
    Done = 1,

    /// <summary>Gave up after the configured number of attempts. See Error.</summary>
    Failed = 2
}
