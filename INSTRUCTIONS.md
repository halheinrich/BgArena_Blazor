# BgArena_Blazor — Subproject Instructions

> Collaboration contract: [`../AGENTS.md`](../AGENTS.md)
> Umbrella status & dependency graph: [`../INSTRUCTIONS.md`](../INSTRUCTIONS.md)
> Mission & principles: [`../VISION.md`](../VISION.md)

## Stack

C# / .NET 10 — Blazor Web App (Interactive Server rendering), xUnit + bUnit +
`Microsoft.AspNetCore.Mvc.Testing` test project. Visual Studio 2026, Windows.

## Solution

`D:\Users\Hal\Documents\Visual Studio 2026\Projects\backgammon\BgArena_Blazor\BgArena_Blazor.slnx`

## Repo

https://github.com/halheinrich/BgArena_Blazor — branch `main`.

## Depends on

- **BgTournament.Api** — the admin HTTP contract: every request/response
  shape the app speaks (summaries, start requests, `ErrorResponse`, the
  replay union). Deliberately the app's **only** shared type surface with the
  tournament server — the server itself is reached over HTTP.
- **BgDiag_Razor** — `BackgammonDiagram`, the view-only board primitive the
  replay renders on.
- **BackgammonDiagram_Lib** — `DiagramRequest` (+ `Builder`), `DiagramMode`;
  the request shape the replay mapper builds (explicit reference per house
  pattern — directly consumed, though also reachable transitively).
- **BgDataTypes_Lib** — `CubeOwner`, the diagram-side cube vocabulary the
  mapper targets (explicit reference per house pattern).

Test-only (never referenced by the app):

- **BgTournament.Server** — the gating smoke boots the real server in-proc
  via `WebApplicationFactory<Program>` (aliased `TournamentServer` in the
  test csproj; `Program` visibility is a test-only `InternalsVisibleTo`
  grant in the producer).
- **BgTournament.EngineClient** — the smoke's reference engines
  (`EngineClient.ServeAsync` over TestServer WebSockets, `RandomPlayAgent`,
  `PassiveCubeAgent`).

## Directory tree

```
BgArena_Blazor.slnx
Directory.Build.props                   warnings-as-errors + XML docs repo-wide
Directory.Packages.props                CPM — inline Version= is banned
INSTRUCTIONS.md
BgArena_Blazor/
├── BgArena_Blazor.csproj
├── Program.cs                          Razor components + the one typed-client registration
├── appsettings.json                    Arena:BaseAddress (required)
├── Properties/launchSettings.json
├── Services/
│   ├── ArenaClient.cs                  typed HTTP client — the eight admin endpoints
│   ├── ArenaResult.cs                  Ok | Refused(status, reason) envelope
│   └── ReplayDiagramMapper.cs          GameEntry/GamePosition → DiagramRequest glue
├── Components/
│   ├── App.razor / Routes.razor / _Imports.razor
│   ├── Layout/                         MainLayout + NavMenu (Engines · Matches · Tournaments)
│   ├── Pages/
│   │   ├── Engines.razor(.cs)          "/" — connected engines, polling
│   │   ├── Matches.razor(.cs)          "/matches" — launch form + newest-first listing
│   │   ├── MatchDetail.razor(.cs)      "/matches/{MatchId}" — record card; polls while Running
│   │   ├── MatchReplay.razor(.cs)      "/matches/{MatchId}/replay" — load-once
│   │   ├── Tournaments.razor(.cs)      "/tournaments" — create form + listing
│   │   └── TournamentDetail.razor(.cs) "/tournaments/{TournamentId}" — standings + ledger
│   └── Shared/
│       ├── PollingComponentBase.cs     the one polling implementation
│       ├── ConnectionBanner.razor      the unreachable-server banner
│       ├── ArenaDisplay.cs             status-CSS / length / score / match-kind wording (SSOT)
│       ├── MatchesTable.razor(.cs)     the one MatchSummary row renderer
│       ├── StandingsTable.razor(.cs)
│       ├── TournamentLedger.razor(.cs)
│       ├── ReplayViewer.razor(.cs)     game picker + entry stepper + captions + board
│       └── ReplayBoard.razor(.cs)      the one sized container over BackgammonDiagram
└── wwwroot/app.css
BgArena_Blazor.Tests/
├── BgArena_Blazor.Tests.csproj
├── ArenaClientTests.cs                 client over canned golden JSON (transport-layer stub)
├── ReplayDiagramMapperTests.cs         frame rule, dice/IsCube split, money sentinel
├── RoutedJsonHandler.cs                per-route stub transport for page tests
├── CannedJson.cs                       shared golden-shaped fixtures (convenience, see Pitfalls)
├── SharedTableTests.cs                 bUnit: tables, links, money wording
├── PageTests.cs                        bUnit: dashboards, forms, refusals, banners
├── ReplayViewerTests.cs                bUnit: stepping, captions, undrawable-position path
├── MatchReplayPageTests.cs             bUnit: page outcomes (golden / 409 / 404)
└── ArenaSmokeTests.cs                  THE gating smoke — real server, real wire, no stubs
```

## Architecture

**What this is.** The tournament arena's admin console and spectator
front-end: polling dashboards over BgTournament's admin HTTP API (engines,
matches, tournaments — with launch/create forms) and step-through replay of
completed matches rendered on `BackgammonDiagram`. v1 is deliberately
poll-based with no live per-move view.

**One route to the server.** `ArenaClient` is the only thing that speaks
HTTP: eight endpoints over one `IHttpClientFactory` typed client whose base
address is configuration (`Arena:BaseAddress` — the UI host calls the
tournament server server-to-server, so CORS never enters). JSON is plain Web
defaults end to end, zero converter configuration — the producer contract
(pinned enum strings, polymorphic replay entries). The refusal model mirrors
the producer's documentation exactly: statuses an endpoint documents
(400/404/409 as applicable) fold into `ArenaResult<T>` — `Ok(value)` or
`Refused(status, reason)`, the reason being the typed `ErrorResponse` body
when one is present — while any undocumented status throws. Listings return
plain lists (only 200 is documented for them).

**One polling implementation.** `PollingComponentBase` owns the loop: initial
load in `OnInitializedAsync`, then a `PeriodicTimer` (2 s) re-running
`RefreshAsync` on the dispatcher. Its failure taxonomy is the client's
documented/undocumented line applied to polling: transport failures
(`HttpRequestException`, timeout) keep the last good data and raise
`IsUnreachable` (the banner) while the loop retries — self-healing; anything
else (a `JsonException` is a contract break) is dispatched into the component
lifecycle via `DispatchExceptionAsync`, never swallowed by the background
task. `ShouldPoll == false` (terminal record) skips ticks without ending the
loop, so a detail page whose route parameter changes resumes by itself;
disposal cancels, then awaits the loop before tearing down.

**Server-authoritative forms.** The launch and create forms encode no request
rules beyond a distinct-engines button gate: they submit and render the
server's typed refusal reason. The rules live once, in the producer;
re-encoding them client-side would be a second source of truth that rots.
The tournament form's participant list is an ordered pick (add / move / 
remove) because order is seeding order — the final standings tie-break.

**Replay: served positions only.** The viewer steps through positions the
server serves — each entry's `state`, then the game's `finalState` — and
never applies a move to a board app-side. `ReplayDiagramMapper` is the whole
app-side glue: every replay position arrives in seat One's frame and is
handed to the diagram unchanged, so seat One (engineOne) is the diagram's
positive/on-roll side for the whole match and **nothing ever flips**. Play
entries render checker-style carrying their dice; cube entries and
`finalState` render cube-style with `[0, 0]` dice — exactly the split
`DiagramRequest.Builder` validates. Money sessions pass `MatchLength = 0`
through (the diagram's own money sentinel) with the needs fields deliberately
0 — the renderer never reads them on that path. Captions name the actor and
action; moves print verbatim in the actor's own numbering, with the
contract's two documented sentinels given their standard notation names
(from 25 = "bar", to 0 = "off").

**Fail visible at the render boundary.** The viewer maps inside a try/catch:
a position the Builder refuses renders an explicit "cannot be rendered"
panel in place of the board while stepping stays alive. No clamping, no
crash — legitimate producer data the renderer can't draw is surfaced, not
massaged.

**Test strategy, by layer.** Unit: the mapper (frame rule, dice split, money
sentinel, cube-owner mapping). Client: `ArenaClient` over a transport-layer
stub (`HttpMessageHandler`, never an interface) so the real serialization
path runs against canned golden-shaped JSON — including a byte-exact pin
that our serialized `StartMatchRequest` matches the producer's golden
request text (the inverse direction of the producer's own pins). Wire
(bUnit): pages over a per-route stub — forms post through the real client,
refusals render, `[EditorRequired]` guards shared-component parameters.
Smoke (gating): `ArenaSmokeTests` boots the real server in-proc, connects
two reference engines over real WebSockets, drives a fixed-seed match to
completion through `ArenaClient`, consumes the replay endpoint's real JSON,
maps every entry and finalState of every game, and renders + steps
`ReplayViewer` through the whole payload. The canned fixtures are
convenience copies; **the smoke is where the contract is proven.**

## Public API

An application, not a library — nothing in this repo is consumed by other
submodules, and all types are app-internal in intent (public only where the
Razor component model requires it). Its outward surface is the UI:

```
/                                   connected engines (claimed/idle), polling
/matches                            launch form + all matches, newest first
/matches/{matchId}                  record card; polls while Running; replay link when Completed
/matches/{matchId}/replay           step-through replay (load-once)
/tournaments                        create form (ordered seeding pick) + all tournaments
/tournaments/{tournamentId}         standings + schedule ledger (rows link to matches)
```

Configuration:

- `Arena:BaseAddress` (required) — the tournament server's HTTP base address;
  startup throws without it. The server's dev default is
  `http://localhost:5251` (see BgTournament's launchSettings).

## Pitfalls

- **The frame rule is fixed — never flip anything.** Every replay position is
  seat-One frame by producer contract; engineOne is always the diagram's
  positive/on-roll side. A seat-Two play therefore shows its dice on seat
  One's board half — cosmetic, the caption carries the actor. Do not "fix"
  this by flipping app-side: the producer's `GameState.OpponentView` is the
  system's single re-expression, and a second one is exactly the bug the
  no-double-flip pins exist to prevent.
- **Moves are printed, never interpreted.** `PlayMove` coordinates are in the
  actor's own numbering. The only permitted translation is presentational:
  the contract's two pinned sentinels (from 25 = bar, to 0 = off). Anything
  that needs to know whose frame a number is in is over the line.
- **The Builder caps `CubeSize` at 4096; the producer does not cap the cube.**
  Unreachable in practice, but a legitimate payload could refuse to render.
  The viewer's mapping try/catch renders the error in place of the board —
  keep that boundary; don't clamp the value and don't let the page crash.
- **`MatchLength == 0` is the money sentinel, and the needs fields are then
  meaningless.** The mapper sets them to 0 on purpose (the renderer's money
  paths never read them). Don't "fix" them to `0 − score` negatives.
- **Canned JSON fixtures are copies, not the contract.** `CannedJson` (and
  the client tests' inline strings) mirror the producer's golden pins for
  convenience. If the smoke is ever weakened, those copies become a silent
  drift surface — the smoke against the real server is the contract proof;
  keep it gating.
- **Poll-failure taxonomy is load-bearing.** Transport failures → banner +
  retry (self-healing). A `JsonException` mid-poll is a producer contract
  break and must escalate (it dispatches into the circuit) — folding it into
  "unreachable" would hide a real break behind a soft banner.
- **Razor markup namespaces come from `_Imports.razor`, not code-behind
  usings.** A component referenced only in markup (e.g. `BackgammonDiagram`)
  fails with RZ10012 unless its namespace is in `_Imports.razor` — the
  code-behind's `using` does not reach the markup.
- **Two top-level-statement `Program` types are visible to the test
  project** (the app's, and the server's via its test-only
  `InternalsVisibleTo` grant). The server reference is aliased
  (`Aliases="TournamentServer"`) and the smoke names
  `TournamentServer::Program`; an unqualified `Program` in the test project
  is a CS0433 waiting to happen.
- **`BackgammonDiagram` has no intrinsic size** (documented producer
  pitfall): its SVG is `viewBox` + `width="100%"`. `ReplayBoard` is the one
  sized container — render through it, don't re-solve sizing per call site.
- **bUnit and the poll loop.** Pages start a 2 s `PeriodicTimer` on init;
  tests assert the initial load via `WaitForAssertion` (well inside the
  first tick) and rely on context disposal to end the loop. Don't write
  tests that wait for a tick — timing-flaky by construction.
- **`Arena:BaseAddress` is fail-fast configuration.** Startup throws if it's
  missing — deliberate; don't default it in code and mask a misconfigured
  deployment.

## Subproject-internal next steps

- **Keyboard stepping on the replay page** (←/→ for prev/next) — the stepper
  is button-only today.
- **Pip counts on replay boards** — `DiagramOptions.ShowPipCount` exists;
  the mapper would need to compute pips from the served Mop (arithmetic, not
  interpretation) or the producer could serve them.
- **Match-list paging/filtering** — the dashboard renders every record;
  fine for v1 volumes, wants bounding once tournaments multiply records.
- **Surface `xgid`** on replay positions when the producer starts populating
  it (schema-reserved, empty today).
- **Per-match decision-timeout field on the launch form** if/when the
  producer adds the per-match override (global-only today).
