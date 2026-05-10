---
name: lmp-client-ui-version
description: Correct in-game Luna Multiplayer version display vs packaging. Use when bumping PATCH in LunaMultiplayer.version or AssemblyInfo, running Scripts/package.ps1, diagnosing "game shows wrong version" (e.g. 0.29.6 UI after tagging 0.29.7), or copying Plugins into Kerbal installs.
---

# LMP client: in-game version and packaging

## What the UI actually shows

The connection/window titles use `LmpVersioning.CurrentVersion` (`LmpClient/Windows/Connection/ConnectionWindow.cs`).

`CurrentVersion` is derived from **`Assembly.GetExecutingAssembly().GetName().Version`** in **`LmpCommon/LmpVersioning.cs`**. That executing assembly for this type is **`LmpCommon.dll`**, not **`LmpClient.dll`**.

Therefore the **major.minor.patch** visible in-game is **`LmpCommon`**’s `AssemblyVersion` (first three segments of `0.N.PATCH.0`). It does **not** come from **`LunaMultiplayer.version`** or from **`LmpClient`**’s AssemblyInfo alone.

Updating only `LunaMultiplayer.version`, or rebuilding only part of the tree, leaves the UI stale if **`LmpCommon.dll`** in **`GameData/LunaMultiplayer/Plugins`** is still an older build.

## After every PATCH bump (checklist)

1. Bump **`LmpCommon/Properties/AssemblyInfo.cs`** `AssemblyVersion` / `AssemblyFileVersion` together with **`LunaMultiplayer.version`** and the other assemblies listed in the stock-interop skill’s version bump section ([`SKILL.md`](../lmp-stock-client-interop/SKILL.md)).
2. **Rebuild** the mod so Plugins pick up **`LmpCommon.dll`**:

   ```powershell
   dotnet build LmpClient\LmpClient.csproj -c Release
   ```

3. Deploy **all** **`LmpClient\bin\Release\*.dll`** into the install’s **`GameData\LunaMultiplayer\Plugins`** (or run **`Scripts/package.ps1`** from a clean tree once nothing holds file locks).

4. Verify the DLL that drives the UI before zipping or hand-off:

   ```powershell
   [System.Reflection.AssemblyName]::GetAssemblyName("$pwd\LmpClient\bin\Release\LmpCommon.dll").Version
   ```

   Expect **`Major.Minor.Build`** (`0`, `29`, `PATCH`) to match the release you intend.

## Packaging / installs

- **Release zips**: `Scripts/package.ps1` copies every **`*.dll`** from **`LmpClient\bin\$Configuration`**; a zip built **before** a post-bump **`LmpCommon`** build still ships **`0.(N).(PATCH-1)**` semantics in-game.
- **Never** partially refresh an install with only **`LmpClient.dll`** unless you’re sure **`LmpCommon.dll`** revision is unchanged — after PATCH bumps it must be replaced too.
- On Windows, **`Compress-Archive`** inside **`package.ps1`** can hit “file in use” on **`Plugins\LmpCommon.dll`** (IDE antivirus, indexer, stray process). Retry with KSP/other handles closed or package into **`dist`** via **`tar -caf`** from a staging tree (same **`GameData`** layout as **`package.ps1`**).

## Symptom → cause

| Symptom | Likely cause |
|--------|----------------|
| In-game patch lags **`LunaMultiplayer.version`** or git tag | Stale **`LmpCommon.dll`** in Plugins / zip built before **`LmpCommon`** rebuild |
| Bump done but **`LmpClient`** shows new ref | **`LmpClient`** AssemblyInfo updated but **`LmpCommon`** not rebuilt or not copied |

## Wire compatibility (unchanged)

`LmpVersioning.IsCompatible` uses major+minor for peers; PATCH does not gate stock **0.29.x** interop. This skill is purely about **display** and packaging consistency — still bump **`PATCH`** consistently across **`LunaMultiplayer.version`**, **`LmpCommon`**, **`LmpClient`**, etc., for supportability and updater clarity.
