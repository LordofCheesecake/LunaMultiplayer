# Fork changes (LordofCheesecake / LunaMultiplayer)

This repository is a fork of [LunaMultiplayer/LunaMultiplayer](https://github.com/LunaMultiplayer/LunaMultiplayer). The list below is everything on **this** default branch that is **not** on `upstream/master` at the time it was last refreshed against GitHub.

**Refresh the list locally**

```text
git fetch upstream
git merge-base upstream/master HEAD
git log upstream/master..HEAD --reverse
```

The merge-base commit is the last shared ancestor with upstream; commits after that on this branch are fork-specific work.

## Compatibility

Major.minor stays `0.29.x`, so stock LunaMultiplayer **0.29.x** clients can connect to this server. Server-side changes were audited so relay paths, vessel channels, and subspace handling stay compatible with stock peers (see `.cursor/skills/lmp-stock-client-interop/SKILL.md` for invariant details).

## Chronological changes (oldest first)

### ef78f01c — Vessel savegame drift and multiplayer stability

- Monotonic `GameTime` guard to reduce out-of-order proto overwrites.
- Lock vessel serialization during backup to avoid torn reads.
- `WarpContext` null reference fixes, lock-query fixes, shutdown/restart races.
- Bounded queues, backpressure, pool recycling improvements.
- Added `BUILDING.md` and `DOCKER.md`.

### ae9ec340 — Building.md update

Documentation tweak for the build guide.

### 021659f2 — Steam specifics

Documentation for Steam-related setup.

### 2548320b — Server setup instructions

Additional server setup documentation.

### 014361dc — Server protocol alignment; deserialization-only poison (v0.29.2)

- Safer casts and debug logging for unknown subtypes in several readers (`LockSystemMsgReader`, `KerbalMsgReader`, `VesselMsgReader`).
- `MessageReceiver`: disconnect threshold applies to deserialization failures only; wrong-type messages are recycled safely.
- Version **0.29.2** (`AssemblyInfo` + `LunaMultiplayer.version`).

### bb378737 — Message recycle race and hardening (v0.29.3)

- Removed per-handler `message.Recycle()` calls so only the central dispatch path recycles wrappers (fixes double-pool enqueue / null subtype vessel spam).
- Soften remaining strict casts and `default:` throws in several readers to debug logs for unknown subtypes.
- `VesselMsgReader`: diagnostic when `message.Data` is null.
- `WarpSystemReceiver.HandleNewSubspace`: force-sync clients to latest subspace when offset was “too early” (later reverted in 0.29.4 for stock interop).
- `VesselResourceDataUpdater`: iterate `ResourcesCount`, null guards, first-match `RESOURCE` lookup.
- `Part.GetFirstModule`: tolerate duplicate module keys; used by part sync / fairing updaters.
- Version **0.29.3**.

### 67057950 — VesselResourceDataUpdater using fix

Adds missing `LunaConfigNode.CfgNode` using in `VesselResourceDataUpdater`.

### 8f6dcfc5 — README and BUILDING

Clearer installation paths, SDK version note, pointers to `INSTALL.md` / `BUILDING.md`.

### be927873 — CI release workflow and packaging

- `.github/workflows/release.yml`: Windows client build, `Scripts/package.ps1`, artifact + Release on `v*` tags.
- `Scripts/package.ps1`: `dist/LunaMultiplayer-x.y.z.zip` with correct `GameData` layout.
- `INSTALL.md` / `BUILDING.md` / `README.md`: install and maintainer documentation updates.

### dd1f8a86 — Restore stock-client interop (v0.29.4)

Reverts or adjusts four areas from 0.29.2/0.29.3 that broke stock peers (vessel stutter / missing in tracking station / subspace sync):

- `VesselMsgReader`: remove lock gates on relay vessel subtypes; always relay proto even when on-disk write is skipped by monotonic `GameTime`.
- `WarpSystemReceiver`: stop rejecting “earlier” subspaces and force-syncing to latest.
- `VesselCliMsg` / `VesselSrvMsg`: single reliable vessel channel (8), not a split channel for Proto.

**Retained** from 0.29.3: idempotent recycle, single wrapper recycle path, deserialization-only poison counter, resource/part hardening, nullable `LatestSubspace` guard.

### 073a880c — Client-side remote vessel smoothing (v0.29.5)

- Client interpolation: playback buffer floor, `MaxInterpolationDuration`, EMA-smoothed time difference, tail-coast when queue is empty; `VesselPositionSystem` tick clamp.
- Server default: `SecondaryVesselUpdatesMsInterval` default **150 → 80** in `IntervalSettingsDefinition` (existing `IntervalSettings.xml` overrides unchanged).

Wire format and relay semantics unchanged for stock **0.29.x** compatibility.

### Integration — upstream `master` + `Release/0_29_2`, merged to default branch (v0.29.6)

- **`upstream/master`**: translations, Swedish loc edits, Linux build script (`Scripts/build-lmp-projects.sh`), KSC/tracking station vessel-list coalescing perf, NAT/buffer tuning, timewarp cap on `WarpMode.None`, maneuver-related client churn, GeoIP tweaks on master server, AppVeyor/README/docs, `.gitignore` cleanup, and other isolated fixes.
- **`upstream/Release/0_29_2`**: Harmony UTC date clamp, contract/scenario client + server churn (sanitizers, migrations, achievements crew dedupe), `DiscoveryInfoSanitizer`, richer `VesselLoader`, logging helpers, `StartLunaServer.bat`, and matching tests where present.
- **Tier A retained (fork “wins” on interop)** — unchanged intent vs `.cursor/skills/lmp-stock-client-interop/SKILL.md`: `Server/Message/VesselMsgReader.cs`, `Server/System/WarpSystemReceiver.cs`, `VesselCliMsg` / `VesselSrvMsg` (reliable vessel channel **8**), and centralized recycle / deserialization-only poison policy; no regressions to relay-only vessel subtypes, proto relay when disk write is skipped, or subspace force-sync.
- **Build alignment post-merge**: `Server` on **net10.0** with `LmpCommon` project reference; fork `LidgrenServer` / `LidgrenMasterServer` surface restored; `ServerTest` → net10.0; `LmpClient` references `LmpCommon.csproj` / `LmpGlobal.csproj` and includes `DockingPortUtil.cs`; small API glue (`SendScenarioModuleImmediate`, `DelayedSendVesselMessage` optional `reason`).

Tagged **v0.29.6**.

---

Current fork version in `LunaMultiplayer.version`: **0.29.6** (PATCH may change; re-open this file or run `git log` above after new tags).
