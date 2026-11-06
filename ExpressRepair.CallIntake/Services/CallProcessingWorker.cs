using ExpressRepair.CallIntake.Data;
using ExpressRepair.CallIntake.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ExpressRepair.CallIntake.Services;

/// <summary>
/// Reads call ids off the queue and runs the pipeline: transcribe, extract, save.
///
/// Runs as a hosted service for the lifetime of the app. Nothing here is on the
/// webhook's request path, which is the point: the provider gets its 200 OK in
/// milliseconds while this takes as long as it needs.
/// </summary>
public sealed class CallProcessingWorker : BackgroundService
{
    private readonly ILeadExtractionQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ProcessingOptions _options;
    private readonly ILogger<CallProcessingWorker> _logger;

    public CallProcessingWorker(
        ILeadExtractionQueue queue,
        IServiceScopeFactory scopeFactory,
        IOptions<ProcessingOptions> options,
        ILogger<CallProcessingWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RequeueStrandedCallsAsync(stoppingToken);

        await foreach (var callId in _queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                await ProcessCallAsync(callId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutting down: stop quietly.
                break;
            }
            catch (Exception ex)
            {
                // Nothing may escape this loop, or one bad call kills the worker
                // and every later call silently stops being processed.
                _logger.LogError(ex, "Unhandled error while processing call {CallId}", callId);
            }
        }
    }

    /// <summary>
    /// The queue lives in memory, so a restart loses anything still in it. On
    /// startup, any call left in Processing is put back on the queue.
    /// </summary>
    private async Task RequeueStrandedCallsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var stranded = await db.Calls
            .Where(c => c.Status == CallStatus.Processing)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        foreach (var id in stranded)
            await _queue.EnqueueAsync(id, cancellationToken);

        if (stranded.Count > 0)
            _logger.LogInformation("Re-queued {Count} call(s) left Processing by a previous run", stranded.Count);
    }

    private async Task ProcessCallAsync(int callId, CancellationToken stoppingToken)
    {
        // A scope per call, because AppDbContext is scoped and must not be
        // shared across concurrent or sequential units of work.
        using var scope = _scopeFactory.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var transcription = scope.ServiceProvider.GetRequiredService<ITranscriptionService>();
        var llm = scope.ServiceProvider.GetRequiredService<ILlmClient>();

        var call = await db.Calls.FirstOrDefaultAsync(c => c.Id == callId, stoppingToken);
        if (call is null)
        {
            _logger.LogWarning("Call {CallId} is on the queue but not in the database", callId);
            return;
        }

        if (call.Status == CallStatus.Done)
            return; // Already handled: a duplicate queue entry is not an error.

        for (var attempt = 1; attempt <= _options.MaxAttempts; attempt++)
        {
            call.Attempts = attempt;

            // One attempt gets its own deadline, so a provider that accepts the
            // connection and then hangs cannot block the queue indefinitely.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.AttemptTimeoutSeconds));

            try
            {
                _logger.LogInformation("Processing call {CallId}, attempt {Attempt}", call.Id, attempt);

                var transcript = await transcription.TranscribeAsync(call.RecordingUrl, timeout.Token);
                call.Transcript = transcript;

                var lead = await llm.ExtractLeadAsync(transcript, timeout.Token);

                call.CustomerName = lead.CustomerName;
                call.Appliance = lead.Appliance;
                call.Brand = lead.Brand;
                call.Problem = lead.Problem;
                call.Urgency = lead.Urgency;
                call.PreferredTime = lead.PreferredTime;

                call.Status = CallStatus.Done;
                call.Error = null;
                call.ProcessedAt = DateTimeOffset.UtcNow;

                await db.SaveChangesAsync(stoppingToken);
                _logger.LogInformation("Call {CallId} done after {Attempts} attempt(s)", call.Id, attempt);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw; // App is shutting down; leave the row Processing to be picked up next start.
            }
            catch (Exception ex)
            {
                var isLastAttempt = attempt == _options.MaxAttempts;
                var reason = Describe(ex, timeout.Token);

                _logger.LogWarning(ex,
                    "Call {CallId} attempt {Attempt}/{Max} failed: {Reason}",
                    call.Id, attempt, _options.MaxAttempts, reason);

                if (!IsRetryable(ex) || isLastAttempt)
                {
                    call.Status = CallStatus.Failed;
                    call.Error = reason;
                    call.ProcessedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(stoppingToken);
                    return;
                }

                // Exponential backoff: 2s, 4s, 8s with the default settings.
                var delay = TimeSpan.FromSeconds(_options.RetryBaseDelaySeconds * Math.Pow(2, attempt - 1));
                await Task.Delay(delay, stoppingToken);
            }
        }
    }

    /// <summary>
    /// The retry rule. Transport and provider problems are worth another try;
    /// a malformed model response is not, because the same transcript will
    /// usually produce the same bad output.
    /// </summary>
    private static bool IsRetryable(Exception ex) => ex switch
    {
        LeadJsonParser.LeadParseException => false,
        TranscriptionException => true,
        LlmException => true,
        TaskCanceledException => true,   // an attempt timeout
        HttpRequestException => true,
        _ => false
    };

    private static string Describe(Exception ex, CancellationToken attemptToken)
    {
        if (ex is OperationCanceledException && attemptToken.IsCancellationRequested)
            return "The provider did not respond before the timeout.";

        // Message only: a stack trace does not belong on a CRM screen.
        return ex.Message;
    }
}
