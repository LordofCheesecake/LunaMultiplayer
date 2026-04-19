# Installing Luna Multiplayer (LMP)

**Players do not need to build anything.** Download or receive a pre-built zip, extract it, and merge the `GameData` folder into your KSP install. Done.

Building from source is only for developers or anyone producing the zip for others.

The dedicated server is separate from the KSP mod. See [SERVER_SETUP.md](SERVER_SETUP.md).

---

## Player install (copy only — no compiler needed)

1. Download `LunaMultiplayer-x.y.z.zip` — see [Where to get a build](#where-to-get-a-build) below.
2. Extract the zip. Inside you will find a `GameData` folder.
3. Copy the contents of that `GameData` folder into your KSP `GameData` folder, so you end up with:
   - `...\Kerbal Space Program\GameData\LunaMultiplayer\`
   - `...\Kerbal Space Program\GameData\000_Harmony\`
4. Start KSP. You should see `[LMP]` in `KSP.log` and the LMP toolbar button.

No Git, no .NET SDK, no Visual Studio.

---

## Where to get a build

| Source | Steps |
|--------|-------|
| **[GitHub Releases](../../releases)** | Go to the Releases page, download the latest `LunaMultiplayer-x.y.z.zip`, extract, merge `GameData` into your KSP folder. |
| **GitHub Actions artifact** | Every push to `main` builds a zip automatically. Go to the [Actions tab](../../actions), open the latest **Build & Release LMP Client** run, scroll to **Artifacts**, download. |
| **Zip from a maintainer** | A maintainer runs `Scripts\package.ps1` locally (see below) and shares the zip file. |

---

## For maintainers — producing the zip

The repo ships two packaging tools so only one person needs the toolchain.

### Option 1 — Local (PowerShell)

After building the client (see [Build from source](#build-from-source-developers-only)):

```powershell
.\Scripts\package.ps1
```

Writes `dist\LunaMultiplayer-x.y.z.zip` with the correct `GameData` tree. Share that file with players.

### Option 2 — GitHub Actions (automated, recommended)

`.github\workflows\release.yml` runs on every push to `main` and every `v*` tag:

- Builds `LmpClient` on a Windows runner (.NET Framework 4.7.2 reference assemblies are pre-installed).
- Extracts `External\KSPLibraries\KSPLibraries.7z` using a **GitHub Secret** for the password.
- Runs `Scripts\package.ps1` and uploads the zip as a workflow artifact.
- On a `v*` tag, also attaches the zip to a GitHub Release automatically.

**One-time secret setup:** in your fork, go to **Settings > Secrets and variables > Actions > New repository secret**, name it `KSP_LIBS_PASSWORD`, and paste the archive password.

After that: push to `main` or push a `v0.29.3` tag and the zip appears in the Actions run (and on the Releases page for tags) for anyone to download.

---

## Build from source (developers only)

Use this when you are changing code or need to produce a new zip yourself.

### Prerequisites

| Requirement | Download |
|-------------|----------|
| .NET SDK 10.x | [dotnet.microsoft.com/download/dotnet/10.0](https://dotnet.microsoft.com/download/dotnet/10.0) |
| .NET Framework 4.7.2 Developer Pack | [dotnet.microsoft.com/download/dotnet-framework/net472](https://dotnet.microsoft.com/download/dotnet-framework/net472) |
| Git for Windows | [git-scm.com](https://git-scm.com/download/win) |

### Clone

```bat
git clone https://github.com/YOUR_FORK/LunaMultiplayer.git
cd LunaMultiplayer
```

### KSP library DLLs

`External\KSPLibraries\KSPLibraries.7z` is password-protected (used by CI). For a local build, copy these files from your KSP installation directly:

**Source:** `<KSP install>\KSP_x64_Data\Managed\`  
**Destination:** `External\KSPLibraries\`

Files to copy:

```
Assembly-CSharp.dll      System.dll               System.Xml.dll
UnityEngine.dll          UnityEngine.AnimationModule.dll
UnityEngine.CoreModule.dll     UnityEngine.ImageConversionModule.dll
UnityEngine.IMGUIModule.dll    UnityEngine.InputLegacyModule.dll
UnityEngine.PhysicsModule.dll  UnityEngine.TextRenderingModule.dll
UnityEngine.UI.dll             UnityEngine.UnityWebRequestModule.dll
```

Use the same KSP version you play with.

### Set your KSP path

Edit `Scripts\SetDirectories.bat`:

```bat
SET KSPPATH=C:\Path\To\Kerbal Space Program
```

Must be an active `SET` (not commented with `::`). Use the folder that contains `KSP_x64.exe`.

### Build and install into GameData

From the **repository root** in **cmd.exe** (not PowerShell):

```bat
dotnet build LmpClient\LmpClient.csproj -c Debug
```

The Debug configuration runs `Scripts\CopyToKSPDirectory.bat` after the build, which copies everything (DLLs, Harmony, all assets) into your KSP `GameData` folder automatically.

To just recopy without rebuilding:

```bat
Scripts\CopyToKSPDirectory.bat
```

### Produce a zip for distribution

```powershell
.\Scripts\package.ps1
```

Output: `dist\LunaMultiplayer-x.y.z.zip` — share this with players.

---

## Quick reference — what belongs in GameData

| Path under GameData | Contents |
|---------------------|----------|
| `LunaMultiplayer\Plugins\` | LMP DLLs and dependencies |
| `000_Harmony\` | Harmony patcher |
| `LunaMultiplayer\Button\` | Toolbar button icon |
| `LunaMultiplayer\Localization\` | Language files |
| `LunaMultiplayer\PartSync\` | Part module sync definitions |
| `LunaMultiplayer\Icons\` | UI icons |
| `LunaMultiplayer\Flags\` | LMP flags |

Full repo-to-GameData mapping: [BUILDING.md](BUILDING.md).

---

## Server

The server is a standalone console app — it does not go inside KSP. See [SERVER_SETUP.md](SERVER_SETUP.md) for Windows (with and without Docker), Mac, and Raspberry Pi instructions.
