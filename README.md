# express-repair-call-intake
## Where AI helped

I used Claude to produce the first working draft of this project from the
brief the project structure, the two service interfaces with their mock and
real implementations, the background worker, and the initial README.

What I did myself: set up the environment, got it building and running, and
debugged the failures that came up. The two real ones were SQLite refusing to
`ORDER BY` a `DateTimeOffset` (fixed by sorting on the auto-increment `Id` and
shaping the response in memory after the query), and a duplicate `/api/calls`
route that stopped the app from starting. I also reviewed each file and can
explain how the webhook, the channel-based queue, the worker's retry rule and
the shared JSON parser fit together, and change any of them.

I have not run this against the real OpenAI or Anthropic APIs — those paths are
written but untested, since the task said no keys would be provided.
