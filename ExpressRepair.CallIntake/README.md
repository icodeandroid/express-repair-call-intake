# Call intake — voicemail to structured lead

A voicemail arrives as a webhook from the telephony provider. The app accepts it
immediately, transcribes and structures it in the background, and the leads page
updates on its own.

## Running it (no keys needed)

```bash
dotnet run
```

Then open <http://localhost:5080>. The default profile is plain HTTP, so there is
no development certificate to trust first; `dotnet run --launch-profile https`
serves HTTPS on port 7080 if you prefer. The app
ships configured for mock providers, creates its own SQLite file on first run,
and needs no API keys, no database server and no internet connection.

On the page, click one of the **Simulate incoming call** buttons. Samples 1–3 are
the three transcripts from the brief. **Sample 4 fails on purpose** — the audio is
unintelligible, the model replies in prose instead of JSON, and the row ends as
`Failed` with the reason on hover. That path is there to be demonstrated, not
hidden.

## Adding real API keys

Nothing is read from a committed file. Switch the provider and supply keys through
user-secrets (local) or environment variables (anywhere else):

```bash
dotnet user-secrets init
dotnet user-secrets set "Ai:Provider" "Real"
dotnet user-secrets set "OpenAI:ApiKey" "sk-..."
dotnet user-secrets set "Anthropic:ApiKey" "sk-ant-..."
```

Or as environment variables:

```bash
export Ai__Provider=Real
export OpenAI__ApiKey=sk-...
export Anthropic__ApiKey=sk-ant-...
```

`appsettings.json` holds empty strings as documentation of the shape only.
`appsettings.Development.json` and `.env` are in `.gitignore`. Set
`Anthropic:Model` to a model your key can access.

## Design decisions

**The webhook does three things: validate, insert, enqueue.** No AI call touches
the request path, so the provider gets its `200 OK` in milliseconds regardless of
how slow transcription is. Returning late is how providers start retrying and
duplicating leads.

**It accepts form-encoded *and* JSON.** Real telephony providers post
`application/x-www-form-urlencoded`; anything we build ourselves sends JSON.
Supporting both costs about ten lines and removes a class of integration
surprise. The handler checks `HasFormContentType` and binds accordingly.

**`CallSid` is a unique index, not just a check in code.** Providers retry
webhooks. A duplicate returns `200` with the existing id rather than creating a
second lead, and the database constraint means two simultaneous deliveries cannot
both slip through.

**The queue is `System.Threading.Channels`, not a broker.** At this volume it is
the simplest thing that satisfies the brief, and a reviewer needs nothing
installed. The trade-off is that queued work is lost on restart — so the worker
sweeps for rows still marked `Processing` on startup and re-queues them.

**Mock and real share the parsing path.** `ILlmClient` implementations obtain raw
text; `LeadJsonParser` does all parsing and validation for both. The mock
therefore exercises the real failure handling rather than a happy path that only
exists in tests. Model output is treated as untrusted input: JSON is extracted
from surrounding prose, `appliance` and `urgency` are forced into their allowed
sets, and `"N/A"` or `"unknown"` become null.

**The retry rule distinguishes transient from permanent.** Network, HTTP and
timeout failures are retried with exponential backoff (2s, 4s, 8s). A malformed
model response is not retried, because the same transcript usually produces the
same bad output; it goes straight to `Failed` with the reason stored on the row.
Each attempt has its own timeout so a hung provider cannot stall the queue.

**Polling, not SignalR.** The brief allows either. At one user and a two-second
interval, polling has no hub, no connection lifecycle and no client library, and
it degrades to "slightly stale" rather than "silently disconnected". The page
re-renders only when the payload actually changes. SignalR would be the right
call at higher scale or with many concurrent viewers.

**No CDN.** Hand-written CSS instead of a Bootstrap link, so the app renders
correctly with no internet connection.

## What I would add before production

- **Webhook signature verification.** The endpoint is currently open; a real
  deployment must verify the provider's HMAC signature and reject anything else.
- **EF migrations** instead of `EnsureCreated`, so schema changes are versioned.
- **A dead-letter view and manual replay** for `Failed` calls, rather than leaving
  them to be found by eye.
- **Authentication and roles** — this screen shows customer phone numbers.
- **Durable queue** (Redis, Azure Service Bus) once losing in-flight work on a
  restart stops being acceptable.
- **Tests.** The parser and the retry rule are pure logic and are the two places
  where a bug would be expensive; both are structured to be unit-testable without
  touching HTTP or the database.
- **Structured logging with a correlation id** carried from webhook to worker.
- **Transcript retention policy** — recordings and transcripts are personal data.

## Where AI helped

I used Claude to draft the initial scaffolding and the boilerplate around the two
HTTP clients, then reworked it myself. The parts I changed most from what it
produced: sharing `LeadJsonParser` between mock and real clients, moving the
retry rule into an explicit `IsRetryable` method rather than retrying everything,
the startup sweep for stranded `Processing` rows, and the decision to route the
sample choice through the recording URL so the simulate button exercises the real
webhook contract rather than a test-only field.

## Project layout

```
Program.cs                     DI wiring and the three endpoints
Models/                        CallRecord, CallStatus, options, DTOs
Data/AppDbContext.cs           EF Core model and constraints
Services/
  ITranscriptionService.cs     + Mock and Whisper implementations
  ILlmClient.cs                + Mock and Claude implementations
  LeadJsonParser.cs            shared parsing and validation
  LeadExtractionQueue.cs       Channel-based hand-off
  CallProcessingWorker.cs      BackgroundService: retry, timeout, persistence
Pages/Index.cshtml             leads table and simulate buttons
wwwroot/                       CSS and the polling script
```

## Endpoints

| Method | Route | Purpose |
| --- | --- | --- |
| `POST` | `/api/calls/recording` | The webhook. Form-encoded or JSON. |
| `GET` | `/api/calls` | Leads as JSON, newest first. Polled by the page. |
| `POST` | `/api/simulate/{1-4}` | Posts a provider-shaped payload to the real webhook. |
