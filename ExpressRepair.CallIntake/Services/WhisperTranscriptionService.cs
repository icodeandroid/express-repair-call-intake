using System.Net.Http.Headers;
using System.Text.Json;
using ExpressRepair.CallIntake.Models;
using Microsoft.Extensions.Options;

namespace ExpressRepair.CallIntake.Services;

/// <summary>
/// Real transcription via OpenAI Whisper.
///
/// Two network calls: download the recording from the provider's URL, then
/// post the bytes to the transcription endpoint as multipart form data.
/// Never runs unless Ai:Provider is "Real" and a key is configured.
/// </summary>
public sealed class WhisperTranscriptionService : ITranscriptionService
{
    private readonly HttpClient _http;
    private readonly OpenAiOptions _options;
    private readonly ILogger<WhisperTranscriptionService> _logger;

    public WhisperTranscriptionService(
        HttpClient http,
        IOptions<OpenAiOptions> options,
        ILogger<WhisperTranscriptionService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> TranscribeAsync(string recordingUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new TranscriptionException(
                "No OpenAI API key configured. Set OpenAI:ApiKey, or set Ai:Provider to \"Mock\".");

        var audio = await DownloadRecordingAsync(recordingUrl, cancellationToken);

        using var form = new MultipartFormDataContent();

        var file = new ByteArrayContent(audio);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        form.Add(file, "file", "recording.mp3");
        form.Add(new StringContent(_options.TranscriptionModel), "model");

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(_options.BaseUrl), "audio/transcriptions"));

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Content = form;

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new TranscriptionException("Could not reach the transcription service.", ex);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // The body may contain the request id, which is what support asks for.
            _logger.LogWarning("Whisper returned {Status}: {Body}", (int)response.StatusCode, body);
            throw new TranscriptionException($"Transcription failed with HTTP {(int)response.StatusCode}.");
        }

        using var document = JsonDocument.Parse(body);

        if (!document.RootElement.TryGetProperty("text", out var text))
            throw new TranscriptionException("Transcription response contained no text field.");

        return text.GetString() ?? string.Empty;
    }

    private async Task<byte[]> DownloadRecordingAsync(string recordingUrl, CancellationToken cancellationToken)
    {
        try
        {
            // In production this request needs the telephony provider's own auth,
            // since recording URLs are normally protected.
            return await _http.GetByteArrayAsync(recordingUrl, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new TranscriptionException($"Could not download the recording from {recordingUrl}.", ex);
        }
    }
}
