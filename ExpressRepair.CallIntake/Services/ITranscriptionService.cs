namespace ExpressRepair.CallIntake.Services;

/// <summary>
/// Turns a recording into text. One method, so swapping Whisper for Deepgram
/// is a DI registration change and nothing else.
/// </summary>
public interface ITranscriptionService
{
    /// <param name="recordingUrl">Where the audio lives, from the webhook.</param>
    Task<string> TranscribeAsync(string recordingUrl, CancellationToken cancellationToken);
}

/// <summary>Transcription failed in a way worth reporting to the user.</summary>
public sealed class TranscriptionException : Exception
{
    public TranscriptionException(string message) : base(message) { }
    public TranscriptionException(string message, Exception inner) : base(message, inner) { }
}
