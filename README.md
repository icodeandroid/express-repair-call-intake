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
serves HTTPS on port 7080 if you prefer. The app ships configured for mock
providers, creates its own SQLite file on first run, and needs no API keys, no
database server and no internet connection.

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
Supporting both costs about ten lines and removes a class of integration surprise.

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

**Sorting by `Id`, not `ReceivedAt`.** SQLite cannot `ORDER BY` a `DateTimeOffset`.
Since `Id` is auto-increment, insertion order is the order we want anyway, and the
response is shaped in memory after the query rather than translated into SQL.

**Polling, not SignalR.** The brief allows either. At one user and a two-second
interval, polling has no hub, no connection lifecycle and no client library, and it
degrades to "slightly stale" rather than "silently disconnected". SignalR would be
the right call at higher scale or with many concurrent viewers.

**No CDN.** Hand-written CSS instead of a Bootstrap link, so the app renders
correctly with no internet connection.

## What I would add before production

- **Webhook signature verification.** The endpoint is currently open; a real
  deployment must verify the provider's HMAC signature.
- **EF migrations** instead of `EnsureCreated`, so schema changes are versioned.
- **A dead-letter view and manual replay** for `Failed` calls.
- **Authentication and roles** — this screen shows customer phone numbers.
- **Durable queue** (Redis, Azure Service Bus) once losing in-flight work on a
  restart stops being acceptable.
- **Tests.** The parser and the retry rule are pure logic and are the two places
  where a bug would be expensive.
- **Structured logging with a correlation id** carried from webhook to worker.
- **Transcript retention policy** — recordings and transcripts are personal data.

## Where AI helped

I used Claude to produce the first working draft of this project from the brief —
the project structure, the two service interfaces with their mock and real
implementations, the background worker, and the initial README.

What I did myself: set up the environment, got it building and running, and
debugged the failures that came up. The two real ones were SQLite refusing to
`ORDER BY` a `DateTimeOffset` (fixed by sorting on the auto-increment `Id` and
shaping the response in memory after the query), and a duplicate `/api/calls`
route that stopped the app from starting. I also reviewed each file and can
explain how the webhook, the channel-based queue, the worker's retry rule and the
shared JSON parser fit together, and change any of them.

I have not run this against the real OpenAI or Anthropic APIs — those paths are
written but untested, since the task said no keys would be provided.

## Project layout


## Endpoints

| Method | Route | Purpose |
| --- | --- | --- |
| `POST` | `/api/calls/recording` | The webhook. Form-encoded or JSON. |
| `GET` | `/api/calls` | Leads as JSON, newest first. Polled by the page. |
| `POST` | `/api/simulate/{1-4}` | Posts a provider-shaped payload to the real webhook. |
