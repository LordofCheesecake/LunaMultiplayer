# LMP Server Setup — Windows, Mac & Raspberry Pi

This guide covers running the LunaMultiplayer game server on Windows (with or without Docker), Mac (Apple Silicon or Intel), and Raspberry Pi.

---

## Windows — without Docker (native exe)

This is the simplest option on Windows. The server is a standalone console application that does not require KSP or any game files.

### Prerequisites

- **.NET SDK 10** — already installed if you followed `BUILDING.md` to build the client. Otherwise download it from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/10.0).

### Build the server

Open `cmd.exe` in the repo root and run:

```bat
dotnet build Server\Server.csproj -c Release
```

Output lands in `Server\bin\Release\net10.0\`.

### Run the server

```bat
cd Server\bin\Release\net10.0
Server.exe
```

The server creates a `Config\` and `Universe\` folder next to the executable on first run. Leave the console window open — closing it stops the server. Press **Ctrl+C** once to trigger a clean shutdown with save backup.

### Open the firewall port

Run this once in an elevated (`Run as administrator`) PowerShell to allow players to connect:

```powershell
New-NetFirewallRule -DisplayName "LMP Server" -Direction Inbound -Protocol UDP -LocalPort 8800 -Action Allow
```

Then forward **UDP 8800** on your router to this machine's local IP.

### Auto-start on boot (optional)

To run the server as a Windows service that starts automatically:

```bat
sc create LMPServer binPath= "\"C:\full\path\to\Server\bin\Release\net10.0\Server.exe\"" start= auto
sc start LMPServer
```

Replace the path with the actual full path on your machine. Use `sc stop LMPServer` to stop it gracefully.

---

## Windows — with Docker

Use this if you want the same container setup as Mac/Raspberry Pi, or want isolation from the host system.

### Prerequisites

- [Docker Desktop for Windows](https://www.docker.com/products/docker-desktop/) with WSL 2 backend (recommended during install).

### Start the server

Open PowerShell or `cmd.exe` in the repo root:

```powershell
docker compose up -d --build
```

### Stop the server

```powershell
docker compose down
```

### Open the firewall port

Docker Desktop handles the Windows firewall automatically for mapped ports. You still need to forward **UDP 8800** on your router to the Windows machine's local IP.

---

## Mac — with Docker

### Prerequisites

Install [Docker Desktop for Mac](https://www.docker.com/products/docker-desktop/) (includes Compose). Works on both Apple Silicon and Intel.

### Get the repository

```sh
git clone https://github.com/LunaMultiplayer/LunaMultiplayer.git
cd LunaMultiplayer
```

### Start the server

```sh
docker compose up -d --build
```

Docker Desktop automatically picks the correct architecture (`linux/arm64` for Apple Silicon, `linux/amd64` for Intel).

### Open the firewall port

macOS has no inbound firewall rule needed by default. Forward **UDP 8800** on your router to the Mac's local IP.

---

## Raspberry Pi — with Docker

### Prerequisites

```sh
curl -fsSL https://get.docker.com | sh
sudo usermod -aG docker $USER
sudo systemctl enable docker
# Log out and back in for the group change to take effect.
```

### Get the repository

```sh
git clone https://github.com/LunaMultiplayer/LunaMultiplayer.git
cd LunaMultiplayer
```

### Start the server

```sh
docker compose up -d --build
```

The image supports `linux/arm64` (Raspberry Pi 4/5 running a 64-bit OS) and `linux/arm/v7` (32-bit OS).

### Open the firewall port

```sh
sudo ufw allow 8800/udp
```

Then forward **UDP 8800** on your router to the Pi's local IP.

---

## Useful Docker commands (all platforms)

```sh
# Follow live server logs
docker compose logs -f lunamultiplayer

# Stop the server (waits up to 30 s to flush the save backup)
docker compose down

# Restart after a config change
docker compose restart lunamultiplayer

# Drop into a shell inside the running container
docker compose exec lunamultiplayer /bin/ash

# Rebuild and restart after a git pull
git pull && docker compose up -d --build
```

---

## Persistent data (Docker)

All server state lives in `.lmp-data/` next to `docker-compose.yml`:

```
.lmp-data/
  Config/     ← server settings (ServerSettings.xml, etc.)
  Universe/   ← saved universe / vessels
  Plugins/    ← optional server-side plugins
  logs/       ← server log files
```

To store data in a different location, create a `.env` file in the repo root:

```
LMP_DATA_DIR=/var/lib/lmp
```

### Match file ownership (Linux hosts)

The container runs as UID/GID 1000 by default. If your host user has a different UID (common on Raspberry Pi), add this to `.env` so bind-mounts are writable:

```sh
echo "LMP_UID=$(id -u)" >> .env
echo "LMP_GID=$(id -g)" >> .env
docker compose up -d --build
```

---

## Connecting from KSP on Windows

In the LMP toolbar button in KSP, enter the server's **public IP address** and port **8800**.

If the server is on the same local network, you can use the server's **local IP** instead (no router port-forward needed).

---

## Ports reference

| Port | Protocol | Purpose |
|---|---|---|
| 8800 | UDP | Game traffic — the port players connect to |
| 8900 | TCP | HTTP admin / server-list endpoint (disabled by default in `docker-compose.yml`) |
