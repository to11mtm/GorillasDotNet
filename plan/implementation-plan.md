# GorillasDotNet — Implementation Plan

Living document. Updated as phases complete. Companion to `base-request.md`.

## Goal

A modern .NET 10 Blazor reimagining of the classic QBasic *Gorillas* artillery duel:
hot-seat first, then server-authoritative online multiplayer with observers, reconnect
catch-up, session replay, and a single-player AI opponent.

## Legal note

`gorillas.bas` is Microsoft-copyrighted sample code. This project implements the *gameplay*
from first principles — ballistic arc, wind, destructible skyline — with original code. The
original is a behavioural reference only; no source is copied or translated.

## Architecture

```
src/Gorillas.Core        Pure deterministic game rules. No I/O, no framework deps.
                         Command -> Decision -> Event -> State fold.
src/Gorillas.Contracts   DTOs + SignalR hub interfaces shared by client and server.
src/Gorillas.Data        Linq2Db + SQLite: schema, repositories, match index/results.
                         Akka.Persistence.Sql shares the same database and provider.
src/Gorillas.Actors      Akka.NET: GameActor (persistent, one per match), LobbyActor.
                         Sole authority over live game state.
src/Gorillas.Server      Blazor Web App (Interactive Server), GameHub, Akka hosting, DI.
src/Gorillas.Client      Razor class library: scene builder, canvas renderer, components.
tests/Gorillas.Core.Tests  xUnit: physics, collision, event fold, determinism, AI.
```

### Key design decisions

- **Determinism is the backbone.** `GameState.Apply(GameEvent) -> GameState` is a pure fold,
  and terrain/wind derive from a seed recorded in `GameCreated`. Server authority, reconnect
  catch-up, live spectating and post-match replay all reduce to "replay the log from
  sequence N" — one design buys four of the requested features.
- **Custom PRNG, not `System.Random`.** `DeterministicRandom` (xorshift64*) guarantees an
  identical sequence across runtimes and framework versions, which `Random` does not.
- **Structural equality on state.** Record equality compares collection members by reference,
  which would make two identically-replayed states compare unequal. `GameState` and `Skyline`
  override equality to compare by value.
- **Server-authoritative.** The client sends `ThrowBanana(angle, velocity)`; the server
  simulates and emits events. Clients only *render* the resulting arc — no trusted client physics.
- **Decide-then-apply split.** Impact events are held back until the flight animation
  completes, so the UI never spoils the outcome. Hot-seat and online use the same mechanism.
- **Rendering.** HTML `<canvas>` via an ES module driven by `requestAnimationFrame`. A fixed
  320x200 virtual resolution is upscaled with nearest-neighbour (`image-rendering: pixelated`).
- **Transport.** SignalR with a strongly-typed client interface. Events carry a monotonic
  sequence number; on reconnect the client sends its last-seen sequence and receives the delta.

### Decisions taken

- **Hosting model:** Blazor Web App, Interactive Server render mode. Client code lives in an
  RCL talking only to `Gorillas.Contracts`, so a WASM swap later needs no rewrite.
- **Identity:** anonymous nickname plus a short shareable game code (e.g. `BAN-7Q3`,
  ambiguous characters excluded). No accounts. A browser-persisted player token lets a
  player reclaim their seat after a disconnect.
- **Visual style:** retro pixel art first. All colours and sprites sit behind a theme seam in
  the renderer, so a modern theme is later a palette swap rather than a rewrite.

---

## Phase 1 — Foundation + hot-seat ✅ DONE

1. ✅ Solution scaffold: five projects, central package management, nullable,
   warnings-as-errors.
2. ✅ `Gorillas.Core`: deterministic PRNG, skyline generator, gorilla placement, wind,
   fixed-timestep trajectory simulator, collision, crater carving, commands/events/state fold,
   round and match scoring.
3. ✅ 46 unit tests: seed determinism, closed-form trajectory verification, wind effects,
   self-hit, crater pass-through, full and partial event-log replay equivalence.
4. ✅ Hot-seat Blazor game at `/play`: canvas renderer, lit windows derived from the seed,
   destructible skyline, wind indicator, throw animation, explosions, scoring, next round.

Verified in headless Chromium: throw → animate → crater → turn-advance with no console errors.

## Phase 2 — Persistence layer ✅ DONE

5. ✅ `Gorillas.Data`: Linq2Db + `Microsoft.Data.Sqlite`, `GorillasDataConnection`, DI
   extension, idempotent schema creation with WAL and indexes.
6. ✅ Append-only event log (`match_events`, PK `(match_id, sequence)`) with optimistic
   concurrency on `matches.last_sequence`, plus a denormalised match/player index so the lobby
   and replay browser never fold a log to answer a question.
7. ✅ 26 integration tests against real temporary SQLite files, including cross-connection
   visibility, competing-writer rejection, and catch-up-from-any-sequence equivalence.

Notes:
- `GameEvent` carries `[JsonPolymorphic]` discriminators — a persisted wire format shared with
  the SignalR transport. Discriminators must never be renamed, only added to.
- Seeds are `ulong` but SQLite has no unsigned integers, so they are stored as reinterpreted
  `long` and cast back.
- linq2db is pinned to 5.4.1.9 to match Akka.Persistence.Sql's dependency.


## Phase 3 — Online multiplayer ✅ DONE

8. ✅ `Gorillas.Actors`: `GameActor : ReceivePersistentActor` (`PersistenceId = game-{id}`) as
   sole authority over a match, and `LobbyActor` owning game codes and rehydrating matches on
   demand. Akka.Persistence.Sql journals to the same SQLite file as the read model.
9. ✅ `GameHub` (SignalR): `CreateGame`, `JoinGame`, `Throw`, `NextRound`, `Forfeit`,
   `Resync(afterSequence)`. The hub decides nothing — it resolves the caller and forwards.
   Broadcasts go to a per-match SignalR group via `IGameEventPublisher`.
10. ✅ `NetworkGameSession` on the client: automatic reconnect, ordered event folding, and a
    queue that holds events back while a banana is mid-flight.
11. ✅ Reconnect, late join and spectating all served by the actor's log from one cursor.

Decisions and notes:
- **The Akka journal is authoritative; `Gorillas.Data` is the query-side projection.** The
  projection tolerates re-delivered events rather than failing, since the journal wins.
- **A custom Akka serializer** (`GameEventAkkaSerializer`) reuses the same System.Text.Json
  encoding as the read model, so journal rows are readable JSON in one already-tested format
  instead of Akka's reflection-based JSON for polymorphic records.
- **No snapshots.** A whole match is ~100 tiny events, so recovery is already instant and this
  avoids serializing the entire `GameState` graph. Revisit if matches grow unbounded.
- **`Akka.Hosting.TestKit` was rejected**: version 1.5.70 is built on xunit v3 and would have
  split the test stack. Tests use `Akka.TestKit.Xunit2` with the plugin's HOCON fallback.
- **`OpenTelemetry.Api` is pinned to 1.17.0** — Akka pulls a 1.10.0 with known advisories.
- **linq2db is pinned to 5.4.1.9** to match Akka.Persistence.Sql's dependency.

Verified with two real browsers driving a live server: seat assignment, code sharing, throws
propagating to the opponent, turn handoff, an observer catching up mid-match, and a reload
reclaiming the same seat and state. 18/18 checks, no console errors.


## Phase 4 — Observers and session replay ✅ DONE

12. ✅ Observer role (landed with Phase 3): read-only subscription, no command rights, live
    spectating with mid-match catch-up, and a participant list.
13. ✅ Replay viewer at `/replays` and `/replay/{id}`: play, pause, step, restart, jump to
    next/previous shot, scrub, and 0.5x–4x speed that also scales the flight animation.

Notes:
- `ReplaySession` implements the same `IGameSession` the live board uses, so replays render
  through exactly the same component and renderer as a real match.
- Seeking is *exact*, not approximate: it re-folds the first N events. This is the payoff of
  the deterministic design, and is covered by a test that seeks to every position in the log
  and compares against an independent fold.
- The catalogue reads the read-model projection rather than the Akka journal, so browsing
  history is a plain indexed SQL query and never disturbs a live match.

### Bug found and fixed during Phase 4
Lobby and aim inputs bound with `@bind:event="oninput"`, so clicking a button immediately
after typing could run the handler before the value round-tripped over the Blazor circuit —
a match got recorded with the host named "Anonymous". Switched to the default `onchange`
binding, which fires on blur before the click. This also affected angle/velocity, where a fast
player could have thrown with a stale value.


## Phase 5 — Single-player AI ✅ DONE

14. ✅ `BallisticSolver` searches for a shot against the *real simulator* rather than solving a
    closed-form parabola, so its answer already accounts for wind, craters and buildings in the
    way. Coarse grid pass plus two refinement passes; short-circuits on a hit.
15. ✅ `GorillaAi` (Easy / Normal / Hard) solves for a good shot then deliberately spoils its
    own aim by an amount that decays with each attempt in the round, so it visibly walks its
    fire in instead of either sniping instantly or flailing forever. Error is roughly normal
    (mean of two uniforms) so near misses are common and wild shots rare.
16. ✅ Runs server-side as an ordinary participant: the lobby seats it via the same `Join` path
    as a human, so solo games are journalled, projected, spectatable and replayable with no
    special cases. Difficulty is recorded on `PlayerJoined`, so a recovered match keeps the
    same opponent.

Notes:
- `PlayerJoined.Difficulty` was added as a *nullable* property. Older logs simply have no value
  and fall back to Normal — an additive, backward-compatible schema change.
- The AI turn uses `IWithTimers` with a keyed single timer rather than `ScheduleTellOnce`, and
  carries the sequence it was scheduled at so a stale timer cannot fire a duplicate throw.

### Bug found and fixed during Phase 5
The lobby is prerendered before the Blazor circuit connects, so a nickname typed in that window
was silently discarded — a match was recorded against "Anonymous" despite the name being typed.
The form now stays disabled until the first interactive render. Akka's own analyzer (AK1004)
also caught an uncancelled scheduled message that would have leaked; fixed with `IWithTimers`.


## Phase 6 — Polish (in progress)

17. ✅ **Shareable links.** `/online?code=BAN-7Q3` with copy-to-clipboard for both an invite and
    a watch link (`&watch=1`). Following an invite auto-joins when a nickname is remembered.
18. ✅ **Sound.** Synthesised with the Web Audio API — no audio assets are shipped.
19. Still to do: modern theme option, touch input, keyboard accessibility, leaderboards,
    CI build and test workflow.

### Shareable links — notes
- An invite link *claims a seat*, so it never auto-joins without a known nickname; a nameless
  visitor gets the code prefilled and is asked to name themselves first. The watch link never
  claims a seat, so it is safe to post publicly.
- Clipboard uses the async API where available and falls back to a hidden textarea, because the
  async API needs a secure context — exactly what plain-HTTP LAN play lacks.

### Sound — notes
- Everything is generated at runtime (oscillators and filtered noise): a falling whistle for the
  flight, a filtered noise burst for impacts with a deeper hit variant, and an arpeggio fanfare.
  This keeps the repository asset-free and suits the chunky retro look.
- Audio only ever fires from the live animation path, so a client catching up after a reconnect
  is silent by construction rather than by special-casing. Covered by a test asserting a
  reconnect replays exactly zero sounds.
- The fanfare is driven off `GamePhase` transitions rather than events, for the same reason.
- Playback speed scales sound alongside the animation, so replays stay in sync at 4x.
- Browsers block audio before a user gesture, so the module unlocks on the first pointer or key
  event and degrades silently if no `AudioContext` is available.

### Bug found and fixed
Auto-join runs from `OnAfterRenderAsync`, which — unlike an event handler — does not trigger a
re-render. The session was created but the UI stayed on the lobby. Fixed by requesting a render
explicitly once the join settles.

## Playtest feedback round ✅ DONE

20. ✅ **Board sizing.** The online board rendered at 368x230 against the hot-seat board's
    956x598. Cause: `.gorillas-shell` uses `margin: 0 auto`, and inside a flex *column* parent
    (the online view) an auto cross-axis margin defeats `align-items: stretch`, so the shell
    collapsed to its content width. Fixed with an explicit `width: 100%`. The canvas is now also
    capped by available viewport height, so it grows to fill the window without pushing the aim
    controls off screen — 800px on a 900px-tall window, 1096px on a 1200px-tall one.
21. ✅ **Discoverability.** Playing the computer was buried inside the online lobby, making it
    look multiplayer-only. It now has its own `/solo` route, a card on a redesigned home page
    alongside online / hot seat / replays, and a persistent nav bar. The online and solo lobbies
    each cross-link to the other.
22. ✅ **Angles beyond 90 degrees.** Throws may now be aimed up to 179 degrees, lobbing the
    banana back over the gorilla's own shoulder — the play that rescues a shot in a strong
    headwind or over a tall neighbouring tower.

Notes on wide angles:
- The physics needed no change: `cos` of an obtuse angle is already negative, so the horizontal
  component reverses naturally.
- The *launch point* did need changing. It was offset by the gorilla's facing, so a backwards
  throw would have started inside its own thrower. It now follows the sign of the horizontal
  velocity.
- The AI searches the full 8–172 degree range, so it will use a backwards lob when the wind
  favours it. Coarse-pass step sizes were widened to keep the doubled search space roughly the
  same cost.

## Documentation ✅ DONE

23. ✅ `README.md`: description, feature list, real captured screenshots (`docs/images/`),
    getting started, configuration, architecture overview and notable decisions.

### Bug found while capturing screenshots
The replay "next shot" button was pinned to the first shot — pressing it repeatedly never
advanced. `SeekToShot` was anchored on a count of shots *played*, but sitting exactly on a
shot's first event means it has not been counted yet, so the arithmetic resolved to the same
target every time. Re-anchored on the cursor: next/previous now find the nearest throw strictly
after/before the current position. Covered by four new tests, including one that walks every
shot in order.

