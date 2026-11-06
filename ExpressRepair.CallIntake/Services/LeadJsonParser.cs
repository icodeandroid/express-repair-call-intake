using System.Text.Json;
using ExpressRepair.CallIntake.Models;

namespace ExpressRepair.CallIntake.Services;

/// <summary>
/// Turns whatever the model actually returned into a validated LeadExtractionResult.
///
/// Both the real and the mock LLM client go through this same method, which
/// means the mock exercises the real failure paths rather than a happy path
/// that only exists in tests.
/// </summary>
public static class LeadJsonParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Thrown when the model's output cannot be turned into a valid lead.
    /// The worker catches this and marks the call Failed.
    /// </summary>
    public sealed class LeadParseException : Exception
    {
        public LeadParseException(string message) : base(message) { }
    }

    public static LeadExtractionResult Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new LeadParseException("The model returned an empty response.");

        var json = ExtractJsonObject(raw);

        Dto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<Dto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            // Truncated so a huge bad response does not end up in the UI.
            throw new LeadParseException($"The model did not return valid JSON: {ex.Message}");
        }

        if (dto is null)
            throw new LeadParseException("The model returned JSON null.");

        // "problem" is the one field we genuinely cannot do without: a lead
        // with no problem description is not a lead.
        if (string.IsNullOrWhiteSpace(dto.Problem))
            throw new LeadParseException("The model returned no problem description.");

        return new LeadExtractionResult(
            CustomerName: Clean(dto.CustomerName),
            Appliance: Normalise(dto.Appliance, LeadExtractionResult.AllowedAppliances, fallback: "other"),
            Brand: Clean(dto.Brand),
            Problem: dto.Problem.Trim(),
            Urgency: Normalise(dto.Urgency, LeadExtractionResult.AllowedUrgencies, fallback: "medium"),
            PreferredTime: Clean(dto.PreferredTime));
    }

    /// <summary>
    /// Models often wrap JSON in prose or a ```json fence even when told not to.
    /// Rather than fail on that, take the outermost {...} block and parse it.
    /// If there is no such block at all, that is a genuine failure.
    /// </summary>
    private static string ExtractJsonObject(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');

        if (start < 0 || end <= start)
            throw new LeadParseException("The model's response contained no JSON object.");

        return raw[start..(end + 1)];
    }

    /// <summary>
    /// Keeps an enum-like field inside its allowed set. An unexpected value is
    /// coerced to the safe fallback rather than rejected, because "other" or
    /// "medium" is still a usable lead and a human can correct it.
    /// </summary>
    private static string Normalise(string? value, string[] allowed, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var candidate = value.Trim().ToLowerInvariant();
        return Array.Exists(allowed, a => a == candidate) ? candidate : fallback;
    }

    /// <summary>
    /// Models like to say "null", "N/A" or "unknown" instead of returning JSON null.
    /// Treat those as missing so the UI shows a clean dash.
    /// </summary>
    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var trimmed = value.Trim();
        return trimmed.ToLowerInvariant() switch
        {
            "null" or "n/a" or "na" or "unknown" or "none" or "-" => null,
            _ => trimmed
        };
    }

    /// <summary>Shape we expect back from the model. Separate from the domain record.</summary>
    private sealed class Dto
    {
        public string? CustomerName { get; set; }
        public string? Appliance { get; set; }
        public string? Brand { get; set; }
        public string? Problem { get; set; }
        public string? Urgency { get; set; }
        public string? PreferredTime { get; set; }
    }
}
