# BgArena_Blazor

> Collaboration contract: [`../AGENTS.md`](../AGENTS.md)
> Umbrella status & dependency graph: [`../INSTRUCTIONS.md`](../INSTRUCTIONS.md)
> Mission & principles: [`../VISION.md`](../VISION.md)
> Open work: the [tracker](https://github.com/halheinrich/backgammon/issues), label `BgArena_Blazor`.

## What it is for

The tournament arena's admin console and spectator front-end: polling
dashboards for engines, matches and tournaments, with launch and create forms;
step-through replay of terminal matches rendered on `BackgammonDiagram`; a
verbatim audit timeline for terminal matches; a `.mat` download; and a
follow-live per-move view of a running match.

It consumes BgTournament's admin HTTP surface, of which it is the only
consumer. Nothing consumes it: it is an application, not a library, and
BgTournament.Server's csproj names it only in an `InternalsVisibleTo` grant to
the test project. Its types are therefore app-internal in intent, public only
where the Razor component model requires it. `DiagramContext` and
`ReplayDiagramMapper` are public without that need; internalizing them is
halheinrich/backgammon#9.

## Stack

C# / .NET 10: a Blazor Web App (Interactive Server rendering) with one
stylesheet (`wwwroot/app.css`) and no JavaScript of its own, and a test project
on xUnit, bUnit and `Microsoft.AspNetCore.Mvc.Testing`. Visual Studio 2026,
Windows.

## Build and test

From this directory:

```bash
dotnet build BgArena_Blazor.slnx
```

```bash
dotnet test BgArena_Blazor.slnx
```

The solution has thirteen projects: this repo's two and, by relative path,
eleven outside ones (BackgammonDiagram_Lib, BgDataTypes_Lib, BgDiag_Razor,
BgGame_Lib, BgMatchFormat_Lib, BgMoveGen, and BgTournament's Api, Core,
EngineClient, Protocol and Server). It needs the umbrella checkout with those
submodules initialized.

The suite reads no `TestData` and no parity model, so nothing in it passes
vacuously on a missing file. The gating smokes (`ArenaSmokeTests`) boot
BgTournament's real server in process with reference engines, and need no
fixture and no running server.

To run the app against a server, start each in its own terminal:

```bash
dotnet run --project ../BgTournament/BgTournament.Server
```

```bash
dotnet run --project BgArena_Blazor/BgArena_Blazor.csproj
```

The app serves `https://localhost:7260`, and its `appsettings.json` already
points `Arena:BaseAddress` at the server's dev port, `http://localhost:5251`
(see "Routes and configuration"). The dashboards stay empty until an engine
connects; the quickest engine is BgTournament's reference bot (its
"Reference-bot executable"):

```bash
dotnet run --project ../BgTournament/BgTournament.ReferenceBot -- --server ws://localhost:5251/engine --name Alpha
```

## Depends on

- **BgTournament.Api** — the admin HTTP contract: every request/response
  shape the app speaks (summaries, start requests, `ErrorResponse`, the
  replay union). Deliberately the app's **only** shared type surface with the
  tournament server — the server itself is reached over HTTP.
- **BgDiag_Razor** — `BackgammonDiagram`, the view-only board primitive the
  replay renders on.
- **BackgammonDiagram_Lib** — `DiagramRequest.ForBoard` and a board's display
  facts (`DisplayFacts`, `DiceFaces`, `MatchRailScore`, `MoneyRailScore`);
  the request the replay mapper builds (explicit reference per house
  pattern — directly consumed, though also reachable transitively).
- **BgDataTypes_Lib** — `BoardPosition`, the board value a served position
  becomes, and `CubeOwner`, the diagram-side cube vocabulary the mapper
  targets (explicit reference per house pattern).

Test-only (never referenced by the app):

- **BgTournament.Server** — the gating smoke boots the real server in-proc
  via `WebApplicationFactory<Program>` (aliased `TournamentServer` in the
  test csproj; `Program` visibility is a test-only `InternalsVisibleTo`
  grant in the producer).
- **BgTournament.EngineClient** — the smoke's reference engines
  (`EngineClient.ServeAsync` over TestServer WebSockets, `RandomPlayAgent`,
  `PassiveCubeAgent`).

## Layout

This repo's two projects are governed by repo-root `Directory.Build.props`
(TFM, `TreatWarningsAsErrors`, XML doc generation) and
`Directory.Packages.props` (Central Package Management — no inline
`Version=`).

**`BgArena_Blazor/`** — the Blazor Web App (Interactive Server). Four areas:

- **Host** — `Program.cs`: the Razor components, the one typed-client
  registration (its base address from the required `Arena:BaseAddress`, and
  the admin key header when `Arena:ApiKey` is set), and the `.mat` export
  relay endpoint; `appsettings.json` and its Development overlay.
- **Services** — `Services/`: `ArenaClient`, the typed HTTP client that is
  the app's only route to the server (admin endpoints, the live SSE
  subscription, the match export); `ArenaResult`, the ok-or-documented-refusal
  envelope; `MatchExportFile`, an export's bytes as served;
  `DiagramContext` and `ReplayDiagramMapper`, the source-agnostic board
  context and the position → board-request mapping the replay and live
  views share.
- **Pages** — `Components/Pages/`, one per route: engines (`/`, polling),
  the match list and launch form, match detail, replay, audit timeline and
  live board, the tournament list and create form, tournament detail; and
  `Error`. `Components/Layout/` holds `MainLayout` and `NavMenu`;
  `App.razor` and `Routes.razor` are the shell.
- **Shared** — `Components/Shared/`: `PollingComponentBase`, the one
  polling implementation; `ConnectionBanner`, the unreachable-server
  banner; the one-renderer tables and indicators (`MatchesTable`,
  `StandingsTable`, `TournamentLedger`, `ClockIndicator`); the replay
  surface (`ReplayViewer`, and `ReplayBoard`, the one sized container over
  `BackgammonDiagram`); and the wording homes — `ArenaDisplay`,
  `ReplayNarration`, `AuditNarration`.

`wwwroot/app.css` is the app's stylesheet.

**`BgArena_Blazor.Tests/`** — xUnit, bUnit and
`Microsoft.AspNetCore.Mvc.Testing`. Areas: the client over canned golden
JSON and its admin-key attachment; the mapper and the display wording;
bUnit page and shared-component tests over `RoutedJsonHandler`, the
per-route stub transport, with `CannedJson`'s golden-shaped fixtures; the
host's `.mat` relay against a scripted upstream; and `ArenaSmokeTests`,
the gating smoke against the real tournament server in-process, the real
wire and reference engines, no stubs. `IsolatedTournamentServer` gives each
in-process tournament server a throwaway journal directory.

## Routes and configuration

The app's outward surface is the UI, which no XML doc carries:

```
/                                   connected engines (in match/idle), polling
/matches                            launch form + all matches, newest first (watch link on Running rows)
/matches/{matchId}                  record card; polls while Running; watch-live when Running, else replay + audit + .MAT
/matches/{matchId}/replay           step-through replay (load-once; partiality note for non-completed)
/matches/{matchId}/audit            arbitration timeline, verbatim rows (load-once; terminal-only, 409 offers /live)
/matches/{matchId}/live             follow-live per-move board over the SSE feed; hands off to replay when terminal
/matches/{matchId}/export.mat       the .MAT download: a relay endpoint in Program.cs, not a page
/tournaments                        create form (ordered seeding pick) + all tournaments, newest first
/tournaments/{tournamentId}         standings + schedule ledger (rows link to matches); polls while Running
```

Configuration:

- `Arena:BaseAddress` (required) — the tournament server's HTTP base address.
  `appsettings.json` ships the server's dev address, `http://localhost:5251`
  (BgTournament's launchSettings); a deployment overrides it.
- `Arena:ApiKey` (optional) — the admin API key this host presents on every
  request, in the header `BgTournament.Api.AdminApiKey.HeaderName` owns.
  Configured once on the typed client, so it covers polling, replay, audit,
  the `.MAT` export relay and the SSE live feed alike. Absent ⇒ anonymous,
  byte-identical requests: the dev default against a keyless server (the beta
  deployment's record is halheinrich/backgammon#6). Present ⇒ the server stamps
  the key's actor name on the durable record of each match and tournament this
  host creates. What the server refuses, and why a mismatch is loud, is
  BgTournament's "Admin identity"; what this app does with the 401 is the first
  pitfall. The value is a secret: it lives only on the client's default
  headers, is never logged, and never reaches a rendered error.

## Architecture

**One route to the server.** `ArenaClient` is the only thing that speaks
HTTP: eleven calls, over eleven server endpoints, on one `IHttpClientFactory`
typed client whose base address is configuration. The UI host calls the
tournament server server-to-server, so CORS never enters. JSON is plain Web
defaults end to end, zero converter configuration — the producer contract
(pinned enum strings, polymorphic replay entries). The refusal model mirrors
the producer's documentation exactly: statuses an endpoint documents
(400/404/409 as applicable) fold into `ArenaResult<T>` — `Ok(value)` or
`Refused(status, reason)`, the reason being the typed `ErrorResponse` body
when one is present — while any undocumented status throws
`HttpRequestException`. Listings return plain lists (only 200 is documented
for them).

**One polling implementation.** `PollingComponentBase` owns the loop: an
initial load, then a 2 s `PeriodicTimer` re-running `RefreshAsync` on the
dispatcher. Its failure taxonomy is the client's documented/undocumented line
applied to polling. An `HttpRequestException` (a transport failure, or an
undocumented status the client threw) or a timeout keeps the last good data
and raises `IsUnreachable`, the banner, while the loop retries, so the page
heals itself. Anything else (a `JsonException` is a contract break) goes into
the component lifecycle through `DispatchExceptionAsync`, never swallowed by
the background task. `ShouldPoll == false` (a terminal record) skips ticks
without ending the loop, so a detail page whose route parameter changes
resumes by itself.

**Server-authoritative forms.** The launch and create forms encode no request
rules; their only client-side gates are buttons disabled until there is
something to submit (two distinct engines, two participants). They submit and
render the server's typed refusal reason. The rules live once, in the
producer; re-encoding them client-side would be a second source of truth that
rots. The help text under each form does state one producer rule, in the
form's words: a supplied seed gives reproducible dice and a blank one fair
dice (BgTournament, "Provably-fair dice"), and an even matches-per-pairing
balances the seats (its "Tournament shape"). The tournament form's participant
list is an ordered pick (add / move / remove) because order is seeding order —
the final standings tie-break.

**Replay: served positions only.** The viewer steps through positions the
server serves — each entry's `state`, then the game's `finalState` — and
never applies a move to a board app-side. `ReplayDiagramMapper` is the whole
app-side glue: every replay position arrives in seat One's frame and is
handed to the diagram unchanged, so seat One (engineOne) is the diagram's
positive/on-roll side for the whole match and **nothing ever flips**.
Captions name the actor and action (`ReplayNarration`, shared with the live
view).

**Every arena board is a board, never a decision.** Each position becomes a
`DiagramRequest.ForBoard` request: the served board as a `BoardPosition`,
and `DisplayFacts` stating only what the API serves. The facts are the
engine names on the rails, the cube's value and owner, and the dice of a
play entry in the served order (cube entries and `finalState` show none).
The score is the other fact. A match board states each side's away score
entering the game and the game's served Crawford flag
(`MatchRailScore`). A money board, the API's match length 0, states the
money label with **no Jacoby rule** (`MoneyRailScore(null)`), because the
API states none. The diagram words the score itself, double match point
included, and counts the pips off the drawn board (BackgammonDiagram_Lib,
"Presentation and rail text"). Nothing of a decision is stated: no
`"Cube Action?"` (decision-only; Hal, 2026-09-28), no analysis, and on a cube
or final step no title or other text in its place (see Pitfalls).

**One mapping, two sources (`DiagramContext`).** The mapper is driven by a
source-agnostic context — engine names and match length (match-level), plus
the entering scores and Crawford flag of the game in view (game-level). The
settled replay builds it from `MatchGamesResponse` + `GameReplay`; the live
feed builds it from `MatchSummary` + the snapshot / game-started event. Six
identical facts either way, so the mapper (and the rail score it states from
them) stays single-sourced. Crawford is a **frame-free** fact the feed
carries, because the Crawford-vs-post-Crawford rule needs match history the
substrate owns.

**Live spectating: follow the latest.** `LiveMatch` subscribes to the SSE
feed and always renders the latest position (no stepping — replay owns that).
It fetches the match summary once for the match-level context, then folds each
event: the snapshot establishes the join-in-progress game (last entry, or a
waiting placeholder), per-move entries render through the shared mapper, a
game-started event shows a between-games placeholder (naming the Crawford
game), and the terminal event shows the outcome and hands off to replay. It is
push not poll, so it is a bespoke component rather than a
`PollingComponentBase`. A transport drop keeps the last board, raises the
unreachable banner and re-subscribes; the fresh snapshot re-establishes state
(BgTournament, "Live feed shape": the feed honors no `Last-Event-ID`). A
malformed event stays out of that path as a `JsonException`, but unlike the
poll loop nothing dispatches it: it faults the watch task, and the page stays
on its last board with no banner until disposal or a change of match id
rethrows it. A timeout (`TaskCanceledException`) takes the same unwatched path.
`ArenaClient.SubscribeMatchLiveAsync` decides the documented refusals at
subscribe time and folds them into the `ArenaResult` envelope; the ordered
stream lives inside `Ok`, and an inner iterator owns the response lifetime.

**Audit: verbatim rows, never joins.** `MatchAudit` renders a terminal
match's arbitration timeline (`GET /matches/{matchId}/audit`) as a flat list
of type-styled rows, one per event, worded by `AuditNarration` (the audit
sibling of `ReplayNarration`). Load-once like replay, and the refusals
mirror it: 404 is an unknown id — or, this endpoint's second documented
flavor, a known record whose journal cannot be read, in which case the 404
carries the server's reason; 409 means still running, and the page offers
the live view. Everything renders verbatim, for an external arbiter:
independent re-verification of the fair-dice fields is declined (see
Pitfalls), and the packet's documented ordering surfaces only as a static
legend on clocked timelines. Computed joins would re-encode the producer's
ordering contract app-side. A clockless (flat-regime) timeline simply has no
clock rows, so the legend is omitted there. The envelope's `integrity` note,
when present, renders as a trusted-prefix warning banner.

**Fail visible at the render boundary.** The replay viewer and the live page
map inside a try/catch for `ArgumentException`, the refusal a position that
cannot be drawn raises: a served board that breaks `BoardPosition`'s
invariant, or a die outside 1–6. Such a position renders an explicit
"cannot be rendered" panel in place of the board while stepping stays
alive. No massaging, no crash: served data the diagram can't draw is
surfaced.

**Test strategy, by layer.** The canned fixtures are convenience copies;
**the smoke is where the contract is proven.**

- **Unit:** the mapper (a board request, never a decision; the frame rule; the
  dice split with no decision-domain text; the cube-owner mapping; the
  Crawford flag and the money label as served; a malformed board refused) and
  the display wording.
- **Client:** `ArenaClient` over a transport-layer stub (`HttpMessageHandler`,
  never an interface), so the real serialization path runs against canned
  golden-shaped JSON, including a byte-exact pin that our serialized
  `StartMatchRequest` matches the producer's golden request text (the inverse
  direction of the producer's own pins). The admin-key attachment has its own
  tests.
- **Wire (bUnit):** pages over a per-route stub. Forms post through the real
  client and refusals render. The replay viewer and live page read the drawn
  board's texts back through the real mapper and renderer: a play step's roll
  and no decision text on a cube or final step, the served Crawford flag,
  double match point worded from the score, the money label with no Jacoby
  rule, and the pip counts.
- **Host:** the `.mat` relay against a scripted upstream.
- **Smoke (gating):** `ArenaSmokeTests`, the real server in process, with
  reference engines on the real wire protocol over TestServer WebSockets.
  - The primary path drives a fixed-seed match to completion through
    `ArenaClient` and consumes the replay endpoint's real JSON, mapping every
    entry and `finalState` of every game to a board request that states the
    game's served Crawford flag, and renders and steps `ReplayViewer` through
    the whole payload. It then consumes the audit endpoint's real JSON
    (created-first / terminal-last, every decision event's replay join inside
    the served replay's bounds, the clockless flat-regime shape) and renders
    the audit page over it.
  - The live path gates the reference engines shut (a `GatedPlayAgent`
    wrapping the seeded random bot), so the subscriber joins while the match
    is parked and catches the stream from the top. It asserts
    snapshot-first / terminal-last ordering, that the games finalized live are
    JSON-identical to the served replay, that every live board renders through
    the page's mapping path, and, while the match is parked, that the audit
    endpoint answers its documented 409 pointing at the live feed.
  - The refusal path reads an unknown engine's typed reason from the real
    server, and the export path pins that the relay's bytes equal the server's
    own export and that its download headers survive.

## Pitfalls

- **401 is the one surface-wide documented refusal.** BgTournament's identity
  gate can answer 401 on any admin endpoint, so `ArenaClient` folds it into
  `ArenaResult` everywhere an envelope is carried (`ArenaClient.AuthRefusal` is
  the single home; `ToResultAsync`, `ExportMatchAsync`, and
  `SubscribeMatchLiveAsync` each honor it) rather than listing it per method.
  The listing calls (`GetEnginesAsync`/`GetMatchesAsync`/`GetTournamentsAsync`)
  return plain lists with no envelope, so a 401 there still throws
  `HttpRequestException` and the poll loop banners it as "unreachable". Widening
  those shapes is halheinrich/backgammon#495; don't paper over it with a
  second path. A fold is only as loud as its caller: the forms and the replay
  and audit pages render the reason, the match detail, tournament detail and
  live pages treat any refusal as not-found, and the `.mat` relay answers a 500.
- **The frame is the producer's — never flip anything.** Every replay
  position is seat-One frame by producer contract. A seat-Two play therefore
  shows its dice on seat One's board half: cosmetic, and the caption carries
  the actor. Do not "fix" this by flipping app-side. That seat-One frame is
  itself a stopgap: Hal ruled that positions stay in the on-roll player's
  frame with no re-expression, and that the renderer places the on-roll
  player instead (halheinrich/backgammon#533). Until
  halheinrich/backgammon#538 brings that, the producer's `ReplayProjection`
  holds the one re-expression left (BgTournament, "The frame rule"); a second
  one here would double-flip.
- **Moves are printed, never interpreted.** `PlayMove` coordinates are in the
  actor's own numbering. The only permitted translation is presentational:
  the contract's two pinned sentinels (from 25 = bar, to 0 = off). Anything
  that needs to know whose frame a number is in is over the line.
- **An arena board states the API's facts and nothing else.** Don't derive
  a Crawford status, don't guess a Jacoby rule for a money board, and don't
  supply double match point; "Every arena board is a board" says why. The
  API's money sentinel (match length 0) is read in the mapper to choose the
  money label; its retirement is BgTournament's, halheinrich/backgammon#317.
- **No title on a cube or final board, not even to hold the strip open.** The
  diagram draws a title strip only for content (BackgammonDiagram_Lib, "Title
  strip"), so a step with none rescales by about 5% against a play step, whose
  strip shows its roll (22 of about 450 units; halheinrich/backgammon#318's
  booking, 2026-09-29). That is accepted (Hal, 2026-09-29, on
  halheinrich/backgammon#273): `DisplayFacts.Title` is content, not a
  geometry lever. Whether stable geometry is wanted stays open on that issue.
- **Keep the render boundary.** Don't massage a position the diagram refuses,
  and don't let the page crash; "Fail visible at the render boundary" says
  what renders instead.
- **Canned JSON fixtures are copies, not the contract.** `CannedJson` (and
  the client tests' inline strings) mirror the producer's golden pins for
  convenience. If the smoke is ever weakened, those copies become a silent
  drift surface — the smoke against the real server is the contract proof;
  keep it gating.
- **The failure taxonomy is load-bearing, polled or pushed.** Transport
  failures banner and retry; a `JsonException` is a producer contract break.
  Folding it into "unreachable" would hide a real break behind a soft banner,
  so don't demote a parse failure, and don't swallow a transport drop into the
  `ArenaResult` envelope. The loops differ in what happens next: "One polling
  implementation" dispatches the break, and "Live spectating" does not.
- **The audit page prints, never correlates.** The packet's ordering semantics
  are the producer's (BgTournament, "Arbitration log"), surfaced only as
  `AuditNarration.ClockCorrelationNote`, a static legend. Do not add
  clock→decision joins or think-time aggregation app-side; if aggregates are
  ever wanted they are a producer-side projection. Fair-dice fields
  (commitment / algorithm / key) render verbatim; an independent re-verify
  badge is declined (ruled in session, 2026-07-04).
- **A clockless audit timeline is normal.** The flat regime measures no
  per-decision timing, so never render the missing clock rows and legend as a
  warning or a gap.
- **Razor markup namespaces come from `_Imports.razor`, not code-behind
  usings.** A component referenced only in markup (e.g. `BackgammonDiagram`)
  fails with RZ10012 unless its namespace is in `_Imports.razor` — the
  code-behind's `using` does not reach the markup.
- **Two top-level-statement `Program` types reach the test project** (the
  app's, and the server's via its test-only `InternalsVisibleTo` grant). The
  server reference is aliased (`Aliases="TournamentServer"`), so an unqualified
  `Program` is the app's and the smoke names `TournamentServer::Program`;
  dropping the alias is a CS0433.
- **`BackgammonDiagram` takes its size from its host** (BgDiag_Razor, "Sizing:
  the ratio, contain-fit and the board slot"). `ReplayBoard` is the one sized
  container — render through it, don't re-solve sizing per call site.
- **bUnit and the poll loop.** Pages start a 2 s `PeriodicTimer` on init;
  tests assert the initial load via `WaitForAssertion` (well inside the
  first tick) and rely on context disposal to end the loop. Don't write
  tests that wait for a tick — timing-flaky by construction.
- **`Arena:BaseAddress` fails on first use, not at boot.** The check sits in
  the typed client's configure callback, so a host missing it starts cleanly
  and answers 500 at the first page that resolves `ArenaClient`. Don't default
  it in code and mask a misconfigured deployment.
