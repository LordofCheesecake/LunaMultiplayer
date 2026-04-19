# Building and Installing LMP on Windows

## Prerequisites

Install the following on your Windows machine:

1. **[.NET SDK](https://dotnet.microsoft.com/download)** — version 8, 9, or 10 all work.
2. **[.NET Framework 4.7.2 Developer Pack](https://dotnet.microsoft.com/download/dotnet-framework/net472)** — provides the `net472` reference assemblies that `LmpClient` targets.
3. **[Git for Windows](https://git-scm.com/download/win)** — to clone the repository.

---

## Step 1 — Clone the repository

Open `cmd.exe` and run:

```bat
git clone https://github.com/<your-fork>/LunaMultiplayer.git
cd LunaMultiplayer
```

---

## Step 2 — Point the build at your KSP install (optional but recommended)

Edit `Scripts\SetDirectories.bat` and uncomment / set the path to your KSP folder:

```bat
SET KSPPATH=C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program
```

If you run two KSP installs you can set `KSPPATH2` as well. This file is intentionally excluded from git commits, so your local path will never be accidentally pushed.

Skipping this step is fine — you will just copy the files manually in Step 4.

---

## Step 3 — Build

From the repository root in `cmd.exe` (not PowerShell):

```bat
Scripts\build-lmp-projects.bat --release
```

This builds `LmpClient`, `Server`, and `MasterServer` in Release mode. To build only one configuration:

```bat
Scripts\build-lmp-projects.bat --debug
```

If `KSPPATH` was set in Step 2, the post-build event automatically copies all output files into your KSP `GameData` folder. You can skip Step 4 entirely.

---

## Step 4 — Copy files to KSP (only needed if you skipped Step 2)

Create the following folder structure inside your KSP directory and copy files as shown:

| Source (inside repo) | Destination (inside KSP) |
|---|---|
| `LmpClient\bin\Release\*.dll` | `GameData\LunaMultiplayer\Plugins\` |
| `External\Dependencies\*.dll` | `GameData\LunaMultiplayer\Plugins\` |
| `External\Dependencies\Harmony\000_Harmony\` | `GameData\000_Harmony\` |
| `LmpClient\Resources\LMPButton.png` | `GameData\LunaMultiplayer\Button\` |
| `LmpClient\Localization\XML\` (entire folder) | `GameData\LunaMultiplayer\Localization\` |
| `LmpClient\ModuleStore\XML\` (entire folder) | `GameData\LunaMultiplayer\PartSync\` |
| `LmpClient\Resources\Icons\` | `GameData\LunaMultiplayer\Icons\` |
| `LmpClient\Resources\Flags\` | `GameData\LunaMultiplayer\Flags\` |

The minimum required DLLs from `LmpClient\bin\Release\` are:

- `LmpClient.dll`
- `LmpCommon.dll`
- `LmpGlobal.dll`
- `Lidgren.Network.dll`
- `CachedQuickLz.dll`
- `System.Buffers.dll`
- `System.Memory.dll`
- `System.Numerics.Vectors.dll`
- `System.Runtime.CompilerServices.Unsafe.dll`

---

## Step 5 — Verify the install

Launch KSP. You should see `[LMP]` log lines in `KSP.log` and the LMP toolbar button in the game UI.

If the mod fails to load, check these common causes:

- **Missing Harmony** — confirm `GameData\000_Harmony\0Harmony.dll` exists.
- **Missing dependency DLL** — confirm all DLLs from `External\Dependencies\` are in `GameData\LunaMultiplayer\Plugins\`.
- **KSP version mismatch** — the `External\KSPLibraries\` references this repo was built against must match your installed KSP version.

---

## Building the server

If you also want to run a local LMP server on the same machine:

```bat
dotnet build Server\Server.csproj -c Release
```

The output lands in `Server\bin\Release\`. Run `Server.exe` from that folder. The server does not need to be installed inside KSP — it is a standalone console application.
