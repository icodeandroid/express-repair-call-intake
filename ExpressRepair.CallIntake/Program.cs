using ExpressRepair.CallIntake.Data;
using ExpressRepair.CallIntake.Models;
using ExpressRepair.CallIntake.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Configuration objects, bound once and injected as IOptions<T>.
// ---------------------------------------------------------------------------
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.Section));
builder.Services.Configure<ProcessingOptions>(builder.Configuration.GetSection(ProcessingOptions.Section));
builder.Services.Configure<OpenAiOptions>(builder.Configuration.GetSection(OpenAiOptions.Section));
builder.Services.Configure<AnthropicOptions>(builder.Configuration.GetSection(AnthropicOptions.Section));

// ---------------------------------------------------------------------------
// Storage. SQLite keeps the project runnable with no database to install,
// while still being a real relational store with real constraints.
// ---------------------------------------------------------------------------
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=calls.db"));

// ---------------------------------------------------------------------------
// Background processing.
// ---------------------------------------------------------------------------
builder.Services.AddSingleton<ILeadExtractionQueue, LeadExtractionQueue>();
builder.Services.AddHostedService<CallProcessingWorker>();

// ---------------------------------------------------------------------------
// AI providers. One switch in configuration decides which pair is registered,
// so no code anywhere else knows whether it is talking to a mock or a provider.
// ---------------------------------------------------------------------------
var provider = builder.Configuration[$"{AiOptions.Section}:Provider"] ?? "Mock";
var useReal = string.Equals(provider, "Real", StringComparison.OrdinalIgnoreCase);

if (useReal)
{
    builder.Services.AddHttpClient<ITranscriptionService, WhisperTranscriptionService>();
    builder.Services.AddHttpClient<ILlmClient, ClaudeLlmClient>();
}
else
{
    builder.Services.AddSingleton<ITranscriptionService, MockTranscriptionService>();
    builder.Services.AddSingleton<ILlmClient, MockLlmClient>();
}

// Used by the simulate endpoint to call our own webhook over real HTTP.
var selfClient = builder.Services.AddHttpClient("self");

if (builder.Environment.IsDevelopment())
{
    // The simulate button calls this app's own URL. Under HTTPS that is the
    // ASP.NET development certificate, which is not trusted on every machine.
    // Scoped to this one named client and to Development only.
    selfClient.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });
}

builder.Services.AddRazorPages();

var app = builder.Build();

// Create the SQLite file and schema on first run. A production app would use
// EF migrations instead; this keeps the reviewer's setup to a single command.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.UseStaticFiles();
app.MapRazorPages();

var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
log.LogInformation("AI provider: {Provider}", useReal ? "Real" : "Mock");

// ===========================================================================
// Task 1 — the webhook.
//
// Accepts either form-encoded or JSON, because real telephony providers post
// application/x-www-form-urlencoded while anything we build ourselves is more
// likely to send JSON. Accepting both costs ten lines and removes a whole
// category of integration surprise.
//
// Does three things only: validate, insert, enqueue. No AI work, no HTTP calls.
// ===========================================================================
app.MapPost("/api/calls/recording", async (
    HttpContext context,
    AppDbContext db,
    ILeadExtractionQueue queue,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    RecordingWebhookRequest? payload;

    if (context.Request.HasFormContentType)
    {
        var form = await context.Request.ReadFormAsync(cancellationToken);
        payload = new RecordingWebhookRequest
        {
            CallSid = form["CallSid"],
            From = form["From"],
            To = form["To"],
            RecordingSid = form["RecordingSid"],
            RecordingUrl = form["RecordingUrl"],
            RecordingDuration = form["RecordingDuration"]
        };
    }
    else
    {
        try
        {
            payload = await context.Request.ReadFromJsonAsync<RecordingWebhookRequest>(cancellationToken);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
        {
            // Malformed JSON, or a content type we cannot read at all.
            return Results.BadRequest(new { error = "Body was neither form-encoded nor valid JSON." });
        }
    }

    if (payload is null || !payload.IsValid())
        return Results.BadRequest(new { error = "CallSid, From and RecordingUrl are required." });

    // Idempotency: providers retry webhooks, and a retry must not create a
    // second lead. Returning 200 on a duplicate also stops the provider
    // retrying forever.
    var existing = await db.Calls
        .FirstOrDefaultAsync(c => c.CallSid == payload.CallSid, cancellationToken);

    if (existing is not null)
    {
        logger.LogInformation("Duplicate webhook for {CallSid}, ignoring", payload.CallSid);
        return Results.Ok(new { id = existing.Id, status = existing.Status.ToString(), duplicate = true });
    }

    var call = new CallRecord
    {
        CallSid = payload.CallSid!,
        From = payload.From!,
        To = payload.To ?? string.Empty,
        RecordingSid = payload.RecordingSid ?? string.Empty,
        RecordingUrl = payload.RecordingUrl!,
        RecordingDuration = int.TryParse(payload.RecordingDuration, out var seconds) ? seconds : 0,
        Status = CallStatus.Processing,
        ReceivedAt = DateTimeOffset.UtcNow
    };

    db.Calls.Add(call);
    await db.SaveChangesAsync(cancellationToken);

    // Enqueue only after the row is committed, or the worker could look for
    // an id that is not there yet.
    await queue.EnqueueAsync(call.Id, cancellationToken);

    logger.LogInformation("Accepted call {CallSid} as id {Id}", call.CallSid, call.Id);

    return Results.Ok(new { id = call.Id, status = call.Status.ToString() });
});

// ===========================================================================
// Task 3 — data for the leads table. Polled by the page.
// ===========================================================================
app.MapGet("/api/calls", async (AppDbContext db, CancellationToken cancellationToken) =>
{
    var calls = await db.Calls
        .AsNoTracking()
        .OrderByDescending(c => c.ReceivedAt)
        .Select(c => new
        {
            c.Id,
            Phone = c.From,
            c.CustomerName,
            c.Appliance,
            c.Brand,
            c.Problem,
            c.Urgency,
            Status = c.Status.ToString(),
            c.ReceivedAt,
            c.Attempts,
            c.Error,
            c.Transcript
        })
        .ToListAsync(cancellationToken);

    return Results.Ok(calls);
});

// ===========================================================================
// Task 3 — "Simulate incoming call".
//
// Posts a provider-shaped, form-encoded payload to our own webhook over real
// HTTP. It deliberately does not call the handler directly or insert a row:
// the point is to exercise the actual endpoint, routing and all.
// ===========================================================================
app.MapPost("/api/simulate/{sample:int}", async (
    int sample,
    HttpContext context,
    IHttpClientFactory httpClientFactory,
    CancellationToken cancellationToken) =>
{
    if (sample is < 1 or > 4)
        return Results.BadRequest(new { error = "Sample must be 1, 2, 3 or 4." });

    // A fresh CallSid each time, so repeated clicks create new leads rather
    // than tripping the duplicate check.
    var suffix = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

    var payload = new Dictionary<string, string>
    {
        ["CallSid"] = $"CA{suffix}",
        ["From"] = "+14165550123",
        ["To"] = "+16475550199",
        ["RecordingSid"] = $"RE{suffix}",
        // The sample number travels in the URL, exactly where a real recording
        // URL would be. The mock transcription service reads it back out.
        ["RecordingUrl"] = $"https://example.com/recordings/sample-{sample}.mp3",
        ["RecordingDuration"] = "47"
    };

    var client = httpClientFactory.CreateClient("self");
    var webhookUrl = $"{context.Request.Scheme}://{context.Request.Host}/api/calls/recording";

    using var response = await client.PostAsync(
        webhookUrl,
        new FormUrlEncodedContent(payload),
        cancellationToken);

    var body = await response.Content.ReadAsStringAsync(cancellationToken);

    return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
});

app.Run();
