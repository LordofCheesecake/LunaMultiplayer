---
name: lmp-stock-client-interop
description: Keep the LordofCheesecake/LunaMultiplayer fork compatible with stock LunaMultiplayer clients, and deploy new server builds to the Raspberry Pi. Use when editing Server/Message/*.cs, LmpCommon/Message/**/*.cs, Server/System/WarpSystem*.cs, or any wire-protocol / lock-gating / subspace code; when diagnosing "vessel not visible", "position stutter", "can't sync time", or poison-message disconnects; or when cutting a new 0.29.x server release for the Pi.
---

# LMP stock-client interop & Pi deploy

Fork `LordofCheesecake/LunaMultiplayer` runs a modified server for a mixed-client game (one modified client, one stock `0.29.x` client). The version check (`LmpVersioning.IsCompatible`) only looks at major+minor, so stock 0.29.x peers connect — but server-side hardening has historically dropped their traffic silently. Four categories of server change break stock interop; always audit new work against them.

## Interop invariants — do NOT add without a kill switch

When editing the files below, do not introduce any of these patterns unless the behavior is feature-flagged off by default.

### 1. Silent vessel-message drops gated on lock ownership
File: `Server/Message/VesselMsgReader.cs`

Vanilla LMP **always relays** Position/Flightstate/Update/Resource/PartSync*/ActionGroup/Fairing/Decouple/Undock. A guard like

```csharp
if (!SenderMayMutateVessel(client, messageData.VesselId)) return; // BAD
```

on these cases silently drops every vessel update from any peer whose lock-acquisition timing doesn't match ours. Symptoms: position **stutter**, **tracking station empty**, "see them in real time but vessel is missing". The stock client's lock flow is slightly different; never add a lock gate on relay paths without a compat flag.

### 2. Suppressing the Proto **relay** on a stale-GameTime write
File: `Server/Message/VesselMsgReader.cs` → `HandleVesselProto`

Keep the stale-GameTime guard on the on-disk write (`RawConfigNodeInsertOrUpdate` may drop), but **always** relay the proto:

```csharp
VesselDataUpdater.RawConfigNodeInsertOrUpdate(id, gameTime, cfg); // may drop write
MessageQueuer.RelayMessage<VesselSrvMsg>(client, msgData);        // always relay
```

Stock clients and late joiners need the live proto stream or their tracking station never spawns the vessel.

### 3. Force-syncing subspaces "earlier than latest"
File: `Server/System/WarpSystemReceiver.cs` → `HandleNewSubspace`

Stock clients legitimately create subspaces behind `WarpContext.LatestSubspace` on reconnect, save-load, or when joining a session where someone has warped ahead. Rejecting these and broadcasting a `WarpChangeSubspaceMsgData` to "correct" the client makes time-sync with the stock peer impossible. Keep only NaN/Infinity + absolute-range sanity bounds on `ServerTimeDifference`.

### 4. Per-subtype Lidgren channel splits
Files: `LmpCommon/Message/Client/VesselCliMsg.cs`, `LmpCommon/Message/Server/VesselSrvMsg.cs`

Both sides must agree on `DefaultChannel`. Vanilla uses a single reliable channel (`8`) for every reliable vessel subtype. Splitting Proto onto its own channel (e.g. `9`) creates asymmetric `ReliableOrdered` guarantees with stock peers, so an `Update` can be processed before its `Proto`. Keep the single-channel mapping.

```csharp
protected override int DefaultChannel => IsUnreliableMessage() ? 0 : 8;
```

## Changes that are safe to keep from the 0.29.2–0.29.4 audit

These address real bugs and don't alter wire semantics with stock peers:

- Idempotent `MessageStore.RecycleMessage` + single `RecycleWrapperOnly` path out of the central dispatch finally (no per-handler `message.Recycle()` calls).
- Deserialization-only poison counter in `Server/Server/MessageReceiver.cs` (handler exceptions log but do not count toward disconnect threshold).
- `WarpContext.LatestSubspace` nullable (returns `FirstOrDefault` so shutdown/reset doesn't throw).
- `VesselResourceDataUpdater` iterating `ResourcesCount` + first-match `RESOURCE` lookup; `Part.GetFirstModule` tolerating duplicate module keys.
- `VesselDataUpdater.RawConfigNodeInsertOrUpdate` monotonic `GameTime` guard on the **write** (not the relay).

## Triage checklist when a stock peer misbehaves

When a player on the stock client reports "I see them in the chat/status but not their vessel", or time-sync refuses to settle:

1. `ssh naundorfit@192.168.178.70 'docker logs --tail 300 lunamultiplayer'` — look for:
   - `Ignoring vessel message subtype … from <player>` spam → a VesselMsgReader guard is dropping something.
   - `Rejecting subspace from <player>: offset … earlier than current latest` → WarpSystemReceiver force-sync is active.
   - `exceeded poison-message threshold` → handler exceptions are counting (should be deserialization-only).
2. `git diff v0.29.4..HEAD -- Server/Message/VesselMsgReader.cs Server/System/WarpSystemReceiver.cs LmpCommon/Message/Client/VesselCliMsg.cs LmpCommon/Message/Server/VesselSrvMsg.cs` — any regression of invariants 1–4 above?
3. If yes, revert those hunks, bump patch version, redeploy (see below).

## Releasing & deploying to the Pi

The Pi (`naundorfit@192.168.178.70`) runs the server from `~/LunaMultiplayer` via Docker Compose. Server data (including `GeneralSettings.xml`) lives in `~/LunaMultiplayer/.lmp-data/` as UID/GID `1000`.

### Version bump (do this for every protocol-adjacent change)

Bump `PATCH` in all of:

- `LunaMultiplayer.version` (JSON `VERSION.PATCH`)
- Every `**/Properties/AssemblyInfo.cs`:
  `Lidgren`, `LmpClient`, `LmpCommon`, `LmpGlobal`, `LmpMasterServer`, `LmpUpdater`, `MasterServer`, `Server` (eight files).
  Replace `AssemblyVersion`, `AssemblyFileVersion`, and `AssemblyInformationalVersion` (where present — some files don't have `AssemblyInformationalVersion`).

One-shot on macOS (replace `0.29.N` → `0.29.N+1`):

```bash
for f in Lidgren/Properties/AssemblyInfo.cs LmpClient/Properties/AssemblyInfo.cs \
         LmpCommon/Properties/AssemblyInfo.cs LmpGlobal/Properties/AssemblyInfo.cs \
         LmpMasterServer/Properties/AssemblyInfo.cs LmpUpdater/Properties/AssemblyInfo.cs \
         MasterServer/Properties/AssemblyInfo.cs Server/Properties/AssemblyInfo.cs; do
  sed -i '' 's/0\.29\.OLD/0.29.NEW/g' "$f"
done
# and manually edit LunaMultiplayer.version
```

### Commit + push + tag

```bash
git add -A && git commit -m "fix(server): <summary>; bump 0.29.N"
git push origin master
git tag -a v0.29.N -m "v0.29.N — <one-line>"
git push origin v0.29.N
```

The `release.yml` workflow builds a Windows client zip when the tag is pushed, **if** `KSP_LIBS_PASSWORD` is set in the fork's Actions secrets. The workflow does **not** publish a server artifact — the Pi builds its own server image from source.

### Deploy to the Pi

```bash
ssh naundorfit@192.168.178.70 '
  cd ~/LunaMultiplayer
  git pull
  docker compose stop lunamultiplayer
  docker compose build lunamultiplayer        # ~60s on Pi
  docker compose up -d lunamultiplayer
  sleep 8
  docker ps --filter name=lunamultiplayer --format "{{.Status}}"
  docker logs --tail 20 lunamultiplayer
'
```

First-time-after-clone only: `sudo chown -R 1000:1000 ~/LunaMultiplayer/.lmp-data` (Docker creates the bind mounts as root otherwise and the container can't write logs/Universe).

### Verify startup logs

Healthy startup block looks like:

```
[LMP]: Luna Server version: 0.29.N (/LMPServer/)
[LMP]: Starting 'Luna DD Server' on Address :: Port 8800...
[LMP]: Master server registration is active
[LMP]: All systems up and running. Поехали!
[Debug]: Detected NAT addresses: 79.254.6.195:8800
```

The NAT-detected address means UPnP on the FritzBox opened UDP 8800 automatically; no manual port-forward is required on this network.

### In-game settings

`~/LunaMultiplayer/.lmp-data/Config/GeneralSettings.xml` on the Pi:

- `ServerName` = `Luna DD Server`
- `Password` = `damdam`
- `MaxPlayers` = `4`

Change via `sed` on the XML then `docker compose restart lunamultiplayer`.

## What NOT to "fix"

Do not chase these — they are benign and their "fixes" historically broke interop:

- "`Ignoring vessel message subtype … from <player>`" in logs — the **cause** of that spam was the per-handler double-recycle race (fixed in 0.29.3). If it reappears only with stock peers and is low-volume, it's just the stock client sending a subtype we now debug-log; do not tighten the default branch back to `throw`.
- Clients on slightly different `GameTime` monotonicity — the write-side guard in `VesselDataUpdater` already handles this. Do not escalate it into a relay-side drop.
- "Stale subspace" from a peer on an older universe time — let `HandleChangeSubspace` do its job; never synthesize a `WarpChangeSubspaceMsgData` on behalf of the client.

## Anchor commits

- `dd1f8a86` (v0.29.4) — the reference "stock-interop-safe" state. Treat diffs from this as the canonical audit baseline for invariants 1–4.
- `bb378737` (v0.29.3) — introduced the four regressions; useful to read its commit message for the reasoning that was later over-applied.
