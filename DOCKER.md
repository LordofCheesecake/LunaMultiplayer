# Running LunaMultiplayer in Docker

Two images are shipped:

| Image | Dockerfile | Purpose | Default ports |
|-------|------------|---------|---------------|
| `lunamultiplayer` | `Dockerfile_Server` | Game server you host for players | `8800/udp`, `8900/tcp` (HTTP admin) |
| `lmpms` | `Dockerfile_MasterServer` | Master server for the LMP federation | `8700/udp`, `8701/tcp` |

Both target Alpine-based self-contained .NET 10 and are multi-arch
(`linux/amd64`, `linux/arm64`, `linux/arm/v7`).

## Quick start with Compose

```sh
# build + start the game server (data persists under ./.lmp-data)
docker compose up -d --build

# follow the logs
docker compose logs -f lunamultiplayer

# stop, letting the server flush its backup (30s grace period)
docker compose down

# attach a shell
docker compose exec lunamultiplayer /bin/ash
```

Override persistence location and timezone via `.env`:

```
LMP_DATA_DIR=/var/lib/lmp
TZ=Europe/Berlin
LMP_UID=1000
LMP_GID=1000
```

To run the master server alongside:

```sh
docker compose --profile master up -d --build
```

## Plain `docker` usage

```sh
# build
docker build -f Dockerfile_Server -t lmpsrv:latest .

# run with persistent state
docker run -d \
  --name lmpsrv \
  -p 8800:8800/udp \
  -p 8900:8900/tcp \
  -v "$PWD/.lmp-data/Config:/LMPServer/Config" \
  -v "$PWD/.lmp-data/Universe:/LMPServer/Universe" \
  -v "$PWD/.lmp-data/Plugins:/LMPServer/Plugins" \
  -v "$PWD/.lmp-data/logs:/LMPServer/logs" \
  --stop-signal SIGINT \
  --stop-timeout 30 \
  --restart unless-stopped \
  lmpsrv:latest
```

The image already drops to a non-root user (`lmp`, UID 1000 by default). When
bind-mounting host directories, `chown 1000:1000` them or set `LMP_UID` /
`LMP_GID` build-args to match your host user:

```sh
docker build -f Dockerfile_Server \
  --build-arg LMP_UID=$(id -u) \
  --build-arg LMP_GID=$(id -g) \
  -t lmpsrv:latest .
```

## Debug shell

`Dockerfile_Server` has a `debug` stage that drops you into an SDK shell with
the source tree available. Useful for `dotnet test`, profiling or
troubleshooting without rebuilding the final image:

```sh
docker build --target debug -f Dockerfile_Server -t lmpsrv:debug .
docker run --rm -it lmpsrv:debug
```

## Build-cache tips

The Dockerfiles use BuildKit [cache mounts](https://docs.docker.com/build/cache/optimize/#cache-mounts)
for `/root/.nuget/packages`. BuildKit is on by default in current Docker
Desktop / Engine; you need nothing extra. In CI, pair with the
`docker/build-push-action` `cache-from` / `cache-to` options (see
`.github/workflows/docker-build.yml`).

## Shutdown semantics

Both images declare `STOPSIGNAL SIGINT`. The server's exit handler listens for
SIGINT and runs `BackupSystem.RunBackup` before closing. Always prefer
`docker stop` (or `docker compose down`) over `docker kill`: the former
delivers SIGINT and waits `stop_grace_period` (30s for the server, 15s for the
master) before escalating to SIGKILL.

## Security posture

The compose file applies several defense-in-depth defaults you may want to
keep when deploying:

- `read_only: true` - container filesystem is immutable; writes go only to the
  declared volumes and a small `/tmp` tmpfs.
- `no-new-privileges` - kernel-level block on SUID escalation.
- `cap_drop: ALL` - no Linux capabilities required.
- Non-root `USER lmp:lmp` - no root inside the container.
- Memory limits via `deploy.resources.limits`.

If any of these bite a plugin you install, relax the specific restriction
rather than disabling all of them.
