namespace ExpressRepair.CallIntake.Models;

/// <summary>
/// The structured lead the LLM is asked to produce from a transcript.
/// Appliance and Urgency are already validated against the allowed sets
/// by the time an instance of this exists — see LeadJsonParser.
/// </summary>
public sealed record LeadExtractionResult(
    string? CustomerName,
    string Appliance,
    string? Brand,
    string Problem,
    string Urgency,
    string? PreferredTime)
{
    public static readonly string[] AllowedAppliances =
        ["fridge", "washer", "dryer", "dishwasher", "oven", "other"];

    public static readonly string[] AllowedUrgencies =
        ["low", "medium", "high"];
}
