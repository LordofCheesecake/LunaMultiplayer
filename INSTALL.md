# Installing Luna Multiplayer (LMP)

There are two ways to get the mod onto your PC:

1. **Download a release** — fastest if you only want to play (no compiler needed).
2. **Clone this repository and build** — for contributors, testers of the latest code, or custom forks.

The game server is separate from the KSP mod. See [SERVER_SETUP.md](SERVER_SETUP.md) to run a server on Windows, Mac, or Raspberry Pi.

---

## Option A — Install from a release (recommended for players)

1. Download the latest **LMP release** for your KSP version from the project’s [GitHub Releases](https://github.com/LunaMultiplayer/LunaMultiplayer/releases) page (or your fork’s releases, if you use one).
2. Extract the archive.
3. Copy the extracted folders into your KSP installation so you end up with:
   - `…\Kerbal Space Program\GameData\LunaMultiplayer\`
   - `…\Kerbal Space Program\GameData\000_Harmony\` (Harmony is required)

Exact layout matches the official wiki: [How to install LMP](https://github.com/LunaMultiplayer/LunaMultiplayer/wiki/How-to-install-LMP).

You do **not** need Git or the .NET SDK for this path.

---

## Option B — Install after cloning and building this repository (Windows)

Use this when you are working from a **git clone** of the source tree and want the mod files generated on your machine.

### 1. Prerequisites

Install on Windows:

| Requirement | Why |
|-------------|-----|
| **[.NET SDK 10.x](https://dotnet.microsoft.com/download/dotnet/10.0)** | Required by `global.json` and the shared libraries (`LmpCommon` targets `net10.0`). |
| **[.NET Framework 4.7.2 Developer Pack](https://dotnet.microsoft.com/download/dotnet-framework/net472)** | Reference assemblies for the KSP mod (`LmpClient` targets `net472`). |
| **Git for Windows** | To clone and update the repo. |

Optional: [GitHub Desktop](https://desktop.github.com/) if you prefer a GUI over the command line.

### 2. Clone the repository

```bat
git clone https://github.com/YOUR_ACCOUNT_OR_UPSTREAM/LunaMultiplayer.git
cd LunaMultiplayer
git pull
```

Use the URL of **this** fork or the upstream repo you actually track.

### 3. KSP library DLLs (`External\KSPLibraries`)

The client project references Unity / KSP assemblies that **cannot** be redistributed in clear form in a public repo. The tree therefore includes `External\KSPLibraries\KSPLibraries.7z`, which is **password-protected** (used by automated builds). **You do not need the password** for a local build.

Copy these files from **your installed KSP** into `External\KSPLibraries\` (same folder as the `.7z`), overwriting nothing else:

**Source folder on disk:**  
`<Your KSP>\KSP_x64_Data\Managed\`

**Files to copy (names must match exactly):**

- `Assembly-CSharp.dll`
- `System.dll`
- `System.Xml.dll`
- `UnityEngine.dll`
- `UnityEngine.AnimationModule.dll`
- `UnityEngine.CoreModule.dll`
- `UnityEngine.ImageConversionModule.dll`
- `UnityEngine.IMGUIModule.dll`
- `UnityEngine.InputLegacyModule.dll`
- `UnityEngine.PhysicsModule.dll`
- `UnityEngine.TextRenderingModule.dll`
- `UnityEngine.UI.dll`
- `UnityEngine.UnityWebRequestModule.dll`

Use the **same KSP version** you play with; mismatched assemblies can cause subtle load or runtime issues.

### 4. Point the copy script at your KSP folder (optional but convenient)

Edit `Scripts\SetDirectories.bat` and set:

```bat
SET KSPPATH=C:\Path\To\Kerbal Space Program
```

Use your real install path (Steam library paths vary). This file is meant to stay local and is not committed in a way that shares your path with others.

### 5. Build and install the mod

From the **repository root**, in **cmd.exe** (not PowerShell), run:

```bat
Scripts\build-lmp-projects.bat --release
```

Or for a Debug build (needed if you rely on the post-build copy step in the client project):

```bat
Scripts\build-lmp-projects.bat --debug
```

**Automatic copy into `GameData`:**  
If `KSPPATH` is set, the **Debug** configuration runs the post-build step that copies the mod into your KSP tree. For **Release**, either:

- Run the copy script after a Debug build:  
  `Scripts\CopyToKSPDirectory.bat`  
  (it copies from `LmpClient\bin\Debug\` — see script), **or**
- Copy files manually as described in [BUILDING.md](BUILDING.md) under “Step 4 — Copy files to KSP”.

### 6. Verify

Start KSP. You should see `[LMP]` lines in `KSP.log` and the LMP button in the UI. If the mod fails to load, check [BUILDING.md](BUILDING.md) “Step 5 — Verify the install” and the [troubleshooting wiki](https://github.com/LunaMultiplayer/LunaMultiplayer/wiki/Troubleshooting).

---

## Quick reference — what ends up in `GameData`

Whether you copy from a release zip or from your own build, a correct install includes at least:

| Location under `GameData` | Contents |
|---------------------------|----------|
| `LunaMultiplayer\Plugins\` | LMP and dependency DLLs |
| `000_Harmony\` | Harmony (`0Harmony.dll`, etc.) |
| `LunaMultiplayer\Button\`, `Localization\`, `PartSync\`, `Icons\`, `Flags\` | Assets from the client tree |

The full manual mapping from repo paths is in [BUILDING.md](BUILDING.md).

---

## Building only the dedicated server

The server does not go inside KSP. After a successful build:

```bat
dotnet build Server\Server.csproj -c Release
```

Run `Server.exe` from `Server\bin\Release\net10.0\`. Details: [SERVER_SETUP.md](SERVER_SETUP.md).
