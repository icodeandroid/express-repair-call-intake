using ExpressRepair.CallIntake.Models;
using Microsoft.Extensions.Options;

namespace ExpressRepair.CallIntake.Services;

/// <summary>
/// Stands in for a real transcription provider so the whole app runs with no keys.
///
/// Which transcript comes back is decided by the recording URL, not by a special
/// parameter: the simulate button sends ".../sample-2.mp3" and this service reads
/// the number out of it. That keeps the webhook contract honest — the request that
/// arrives is shaped exactly like a real provider's, with no test-only fields.
/// </summary>
public sealed class MockTranscriptionService : ITranscriptionService
{
    private readonly ProcessingOptions _options;
    private readonly ILogger<MockTranscriptionService> _logger;

    /// <summary>The three transcripts from the brief, plus one that cannot be used.</summary>
    public static readonly IReadOnlyDictionary<int, string> Samples = new Dictionary<int, string>
    {
        [1] = "Hi, this is Maria Lopez. My Samsung fridge stopped cooling since yesterday, " +
              "all my food is going bad. Please call me back as soon as possible, I'm home all day.",

        [2] = "Hello, I have a Whirlpool washer, it doesn't drain water at the end of the cycle. " +
              "No rush, maybe next week in the morning. My name is John.",

        [3] = "Yeah hi... uh, the oven, it's making a weird noise, I don't know the brand. Call me.",

        // Deliberate failure case, so the Failed path can be demonstrated from the browser.
        [4] = "[unintelligible audio]"
    };

    public MockTranscriptionService(
        IOptions<ProcessingOptions> options,
        ILogger<MockTranscriptionService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> TranscribeAsync(string recordingUrl, CancellationToken cancellationToken)
    {
        // Stand in for network latency, so a human can actually see the
        // Processing badge before it flips to Done.
        await Task.Delay(_options.MockLatencyMilliseconds, cancellationToken);

        var sample = ParseSampleNumber(recordingUrl);
        _logger.LogInformation("Mock transcription returning sample {Sample} for {Url}", sample, recordingUrl);

        return Samples.TryGetValue(sample, out var transcript)
            ? transcript
            : Samples[1];
    }

    /// <summary>Pulls the N out of a URL ending in "sample-N.mp3". Defaults to 1.</summary>
    private static int ParseSampleNumber(string recordingUrl)
    {
        const string marker = "sample-";

        var index = recordingUrl.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return 1;

        var digits = new string(recordingUrl[(index + marker.Length)..]
            .TakeWhile(char.IsDigit)
            .ToArray());

        return int.TryParse(digits, out var n) ? n : 1;
    }
}
