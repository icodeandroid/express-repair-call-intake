using ExpressRepair.CallIntake.Models;

namespace ExpressRepair.CallIntake.Services;

/// <summary>
/// Turns a transcript into a structured lead. Implementations are responsible
/// for getting raw text out of a model; parsing and validating it is shared
/// (see <see cref="LeadJsonParser"/>) so both paths behave identically.
/// </summary>
public interface ILlmClient
{
    Task<LeadExtractionResult> ExtractLeadAsync(string transcript, CancellationToken cancellationToken);
}

/// <summary>The model call itself failed: network, auth, rate limit, timeout.</summary>
public sealed class LlmException : Exception
{
    public LlmException(string message) : base(message) { }
    public LlmException(string message, Exception inner) : base(message, inner) { }
}
