using System.Text.Json;
using ExpressRepair.CallIntake.Models;
using Microsoft.Extensions.Options;

namespace ExpressRepair.CallIntake.Services;

/// <summary>
/// Stands in for a real model. It builds a JSON string with simple keyword rules
/// and then hands that string to the same <see cref="LeadJsonParser"/> the real
/// client uses, so the parsing and validation code is exercised either way.
///
/// For the unintelligible sample it returns deliberately malformed output, which
/// is how the Failed path can be demonstrated without unplugging anything.
/// </summary>
public sealed class MockLlmClient : ILlmClient
{
    private readonly ProcessingOptions _options;
    private readonly ILogger<MockLlmClient> _logger;

    public MockLlmClient(IOptions<ProcessingOptions> options, ILogger<MockLlmClient> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<LeadExtractionResult> ExtractLeadAsync(string transcript, CancellationToken cancellationToken)
    {
        await Task.Delay(_options.MockLatencyMilliseconds, cancellationToken);

        var raw = BuildRawResponse(transcript);
        _logger.LogInformation("Mock LLM raw response: {Raw}", raw);

        // Same parser, same validation, same exceptions as the real client.
        return LeadJsonParser.Parse(raw);
    }

    private static string BuildRawResponse(string transcript)
    {
        var text = transcript.ToLowerInvariant();

        // The unintelligible sample: a real model asked to extract a lead from
        // noise often apologises in prose instead of returning JSON. Reproduce that.
        if (text.Contains("unintelligible"))
            return "I'm sorry, the audio was not clear enough for me to extract any lead details.";

        var appliance = text switch
        {
            _ when text.Contains("fridge") || text.Contains("refrigerator") => "fridge",
            _ when text.Contains("washer") || text.Contains("washing machine") => "washer",
            _ when text.Contains("dryer") => "dryer",
            _ when text.Contains("dishwasher") => "dishwasher",
            _ when text.Contains("oven") || text.Contains("stove") => "oven",
            _ => "other"
        };

        var brand = FindFirst(text, ["samsung", "whirlpool", "lg", "bosch", "ge", "maytag", "frigidaire"]);

        var urgency = text switch
        {
            _ when text.Contains("as soon as possible") || text.Contains("asap")
                || text.Contains("going bad") || text.Contains("emergency") => "high",
            _ when text.Contains("no rush") || text.Contains("next week") => "low",
            _ => "medium"
        };

        string? customerName = text switch
        {
            _ when text.Contains("maria lopez") => "Maria Lopez",
            _ when text.Contains("my name is john") => "John",
            _ => null
        };

        string? preferredTime = text switch
        {
            _ when text.Contains("home all day") => "Any time today",
            _ when text.Contains("next week in the morning") => "Next week, mornings",
            _ => null
        };

        var problem = appliance switch
        {
            "fridge" => "Fridge has stopped cooling since yesterday and food is spoiling",
            "washer" => "Washer does not drain water at the end of the cycle",
            "oven" => "Oven is making an unusual noise",
            _ => "Customer reports an appliance fault"
        };

        // Written out as JSON on purpose: this is what a model hands back.
        return JsonSerializer.Serialize(new
        {
            customerName,
            appliance,
            brand,
            problem,
            urgency,
            preferredTime
        });
    }

    private static string? FindFirst(string text, string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (text.Contains(candidate))
                return char.ToUpperInvariant(candidate[0]) + candidate[1..];
        }
        return null;
    }
}
