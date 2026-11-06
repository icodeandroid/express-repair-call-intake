namespace ExpressRepair.CallIntake.Models;

/// <summary>Which pair of implementations to wire up: "Mock" or "Real".</summary>
public sealed class AiOptions
{
    public const string Section = "Ai";
    public string Provider { get; set; } = "Mock";
}

/// <summary>Retry and timeout rules for the background worker.</summary>
public sealed class ProcessingOptions
{
    public const string Section = "Processing";

    /// <summary>Total tries per call, including the first one.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Base delay between tries; doubled each attempt (2s, 4s, 8s...).</summary>
    public int RetryBaseDelaySeconds { get; set; } = 2;

    /// <summary>Hard ceiling on one attempt, so a hung provider cannot stall the queue.</summary>
    public int AttemptTimeoutSeconds { get; set; } = 30;

    /// <summary>Artificial delay in the mocks, so the Processing state is visible in the UI.</summary>
    public int MockLatencyMilliseconds { get; set; } = 1500;
}

/// <summary>Credentials for the real transcription provider (OpenAI Whisper).</summary>
public sealed class OpenAiOptions
{
    public const string Section = "OpenAI";
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";
    public string TranscriptionModel { get; set; } = "whisper-1";
}

/// <summary>Credentials for the real LLM provider (Anthropic Claude).</summary>
public sealed class AnthropicOptions
{
    public const string Section = "Anthropic";
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.anthropic.com/v1/";
    public string Model { get; set; } = "claude-sonnet-4-5";
    public string Version { get; set; } = "2023-06-01";
    public int MaxTokens { get; set; } = 512;
}
