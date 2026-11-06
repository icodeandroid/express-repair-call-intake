using System.Net.Http.Json;
using System.Text.Json;
using ExpressRepair.CallIntake.Models;
using Microsoft.Extensions.Options;

namespace ExpressRepair.CallIntake.Services;

/// <summary>
/// Real lead extraction via the Anthropic Messages API.
///
/// The prompt is deliberately strict about returning only JSON, but the parser
/// does not trust it to comply — see <see cref="LeadJsonParser"/>. Treating model
/// output as untrusted input is the whole reason this stays reliable.
/// </summary>
public sealed class ClaudeLlmClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly AnthropicOptions _options;
    private readonly ILogger<ClaudeLlmClient> _logger;

    private const string SystemPrompt = """
        You extract structured repair leads from voicemail transcripts for an
        appliance repair company.

        Return ONLY a JSON object, with no prose and no markdown fence, shaped:
        {
          "customerName": string or null,
          "appliance": one of "fridge","washer","dryer","dishwasher","oven","other",
          "brand": string or null,
          "problem": short description,
          "urgency": one of "low","medium","high",
          "preferredTime": string or null
        }

        Rules:
        - Use null for anything the caller did not say. Never invent a value.
        - urgency is "high" when the caller asks for a fast callback or describes
          spoiling food, a leak or anything unsafe; "low" when they say there is
          no rush; otherwise "medium".
        - If the transcript is empty or unintelligible, still return the JSON object
          with "problem" describing that the audio was unusable.
        """;

    public ClaudeLlmClient(
        HttpClient http,
        IOptions<AnthropicOptions> options,
        ILogger<ClaudeLlmClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<LeadExtractionResult> ExtractLeadAsync(string transcript, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new LlmException(
                "No Anthropic API key configured. Set Anthropic:ApiKey, or set Ai:Provider to \"Mock\".");

        var payload = new
        {
            model = _options.Model,
            max_tokens = _options.MaxTokens,
            system = SystemPrompt,
            messages = new[]
            {
                new { role = "user", content = $"Transcript:\n\n{transcript}" }
            }
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(_options.BaseUrl), "messages"))
        {
            Content = JsonContent.Create(payload)
        };

        request.Headers.Add("x-api-key", _options.ApiKey);
        request.Headers.Add("anthropic-version", _options.Version);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new LlmException("Could not reach the language model service.", ex);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Anthropic returned {Status}: {Body}", (int)response.StatusCode, body);
            throw new LlmException($"Lead extraction failed with HTTP {(int)response.StatusCode}.");
        }

        var raw = ReadFirstTextBlock(body);
        _logger.LogInformation("LLM raw response: {Raw}", raw);

        // Parsing and validation are shared with the mock client.
        return LeadJsonParser.Parse(raw);
    }

    /// <summary>
    /// The Messages API returns content as an array of blocks; the text we want
    /// is the first block of type "text".
    /// </summary>
    private static string ReadFirstTextBlock(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            if (!document.RootElement.TryGetProperty("content", out var content))
                throw new LlmException("Model response contained no content array.");

            foreach (var block in content.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var type) &&
                    type.GetString() == "text" &&
                    block.TryGetProperty("text", out var text))
                {
                    return text.GetString() ?? string.Empty;
                }
            }

            throw new LlmException("Model response contained no text block.");
        }
        catch (JsonException ex)
        {
            throw new LlmException("Model response was not valid JSON at the envelope level.", ex);
        }
    }
}
