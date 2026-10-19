# Spec: container image pipeline (GHCR + Docker Hub)

**Status:** Draft — for review before implementation
**Scope:** a GitHub Actions workflow that builds the AudioYotoShelf image and publishes it to GitHub
Container Registry and Docker Hub, plus the image and app changes needed so the published image can
be configured entirely from `docker run -e …` / Compose `environment:` at container start.

## 1. Goals and non-goals

**Goals**

1. Every merge to `main` publishes an image; every `vX.Y.Z` tag publishes a versioned release image.
2. The same image goes to `ghcr.io/o-mutt/audioyotoshelf` and `docker.io/<namespace>/audioyotoshelf`.
3. Images are multi-arch (`linux/amd64`, `linux/arm64`) so homelab users on ARM (Raspberry Pi,
   Apple Silicon, Ampere) can pull the same tag.
4. A user can run the published image without cloning the repo or knowing ASP.NET's `Section__Key`
   naming: every setting needed to run the app has a flat, documented environment variable.
5. Pull requests build the image (catching Dockerfile breakage) and smoke-test it, but never push.

**Non-goals**

- Changing the app's internal port, adding TLS termination inside the container, or running as root
  to support `PUID`/`PGID` remapping (the image keeps the .NET base image's non-root `app` user).
- Automating release versioning (release-please, changelog generation). Tags are cut by hand.
- Publishing Postgres/Redis images — users keep using the upstream ones.

## 2. Current state (what this builds on)

- `Dockerfile` is already a 3-stage build (Node SPA → .NET publish → `aspnet:10.0` + ffmpeg), runs
  as `$APP_UID`, exposes `8080`, and has a `/health/live` `HEALTHCHECK`.
- `.github/workflows/ci.yml` runs backend, frontend, integration and e2e jobs on PRs and `main`. No
  workflow builds or pushes an image today; `docker-compose.yml` only uses `build: .`.
- No git tags or releases exist yet.
- **Configuration gap:** the app only reads ASP.NET configuration keys (`Yoto__ClientId`,
  `ConnectionStrings__Postgres`, …). The friendly names in `.env.example` (`YOTO_CLIENT_ID`,
  `DB_PASSWORD`, …) work *only* because `docker-compose.yml` translates them. Someone running the
  published image directly with `-e YOTO_CLIENT_ID=…` gets an app that ignores it. (The lone
  exception is `ADMIN_USERNAMES`, which `AuthController` reads as a fallback.) Closing this gap is
  part of this spec — see §5.
- **Reverse-proxy gap:** the Yoto and ABS SSO callback URLs are built from `Request.Scheme` /
  `Request.Host`, and no forwarded-headers middleware is configured. Behind a TLS-terminating proxy
  the app builds `http://…/api/auth/yoto/callback`, which won't match the `https://` URL registered
  with Yoto. Most published-image users will sit behind a proxy, so §5 exposes a switch for this.

## 3. Workflow: `.github/workflows/docker.yml`

A new workflow, separate from `ci.yml`, so image publishing has its own permissions
(`packages: write`) and its own trigger on tags, and `ci.yml` stays read-mostly.

### 3.1 Triggers

| Event | Builds | Pushes | Tags produced |
|-------|--------|--------|---------------|
| `pull_request` → `main` | amd64 only (fast), loaded locally for the smoke test | no | — |
| `push` → `main` | amd64 + arm64 | yes | `edge`, `sha-<7>` |
| `push` tag `v*.*.*` | amd64 + arm64 | yes | `X.Y.Z`, `X.Y`, `X`, `latest`, `sha-<7>` |
| `workflow_dispatch` | amd64 + arm64 | yes | `sha-<7>` (+ `edge` when run on `main`) |

- `latest` tracks the newest **release**, not `main`, so `docker compose pull` on `latest` never
  picks up unreleased work. `edge` is the opt-in rolling tag.
- Pre-release tags (`v1.2.0-rc.1`) publish `1.2.0-rc.1` only — no `latest`/`X.Y`/`X`
  (`docker/metadata-action` handles this via its semver rules).
- `concurrency: docker-${{ github.ref }}` with `cancel-in-progress: true` for PRs and `main`; never
  cancel tag builds.
- Paths filter on PRs: skip when only `**/*.md`, `docs/**`, `tests/**` or `.github/ISSUE_TEMPLATE/**`
  change (none of them are in the build context — see `.dockerignore`).

**Gating on CI.** Image builds for `main` run in parallel with `ci.yml` rather than via
`workflow_run`: changes reach `main` only through PRs that already passed CI, and `workflow_run`
complicates tag builds and always runs the workflow file from the default branch. Release tags are
cut deliberately from a green `main`. If stricter gating is wanted later, fold the publish job into
`ci.yml` with `needs: [backend, frontend, integration, e2e]`.

### 3.2 Permissions

```yaml
permissions:
  contents: read
  packages: write        # push to ghcr.io
  id-token: write        # keyless signing / provenance attestation
  attestations: write    # actions/attest-build-provenance
```

PR runs (including from forks) receive a read-only token and no secrets, which is fine because they
never log in or push.

### 3.3 Secrets and variables

| Name | Kind | Required | Purpose |
|------|------|----------|---------|
| `GITHUB_TOKEN` | built-in | — | Log in to `ghcr.io` |
| `DOCKERHUB_USERNAME` | secret | for Docker Hub | Docker Hub account that owns the token |
| `DOCKERHUB_TOKEN` | secret | for Docker Hub | Docker Hub access token (Read & Write scope), **not** the account password |
| `DOCKERHUB_REPOSITORY` | repo variable | for Docker Hub | e.g. `omutt/audioyotoshelf` |

If `DOCKERHUB_TOKEN` is unset, the workflow publishes to GHCR only (the Docker Hub login and image
name are added conditionally), so the pipeline works before the Docker Hub account is set up.

GHCR packages created by Actions start **private**. After the first publish, set the package to
public once under *Package settings → Change visibility* and link it to the repo.

### 3.4 Job outline

```yaml
jobs:
  image:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
      - uses: docker/setup-qemu-action@v3      # only the runtime stage's apt-get runs emulated (§4.1)
      - uses: docker/setup-buildx-action@v3

      - name: Log in to GHCR
        if: github.event_name != 'pull_request'
        uses: docker/login-action@v3
        with: { registry: ghcr.io, username: ${{ github.actor }}, password: ${{ secrets.GITHUB_TOKEN }} }

      - name: Log in to Docker Hub
        if: github.event_name != 'pull_request' && secrets.DOCKERHUB_TOKEN != ''   # via an env indirection
        uses: docker/login-action@v3
        with: { username: ${{ secrets.DOCKERHUB_USERNAME }}, password: ${{ secrets.DOCKERHUB_TOKEN }} }

      - id: meta
        uses: docker/metadata-action@v5
        with:
          images: |
            ghcr.io/o-mutt/audioyotoshelf
            docker.io/${{ vars.DOCKERHUB_REPOSITORY }}     # omitted when Docker Hub is not configured
          tags: |
            type=semver,pattern={{version}}
            type=semver,pattern={{major}}.{{minor}}
            type=semver,pattern={{major}}
            type=raw,value=edge,enable={{is_default_branch}}
            type=sha,format=short
          # `latest` comes from metadata-action's default: on for semver tags only (flavor latest=auto).

      - id: build
        uses: docker/build-push-action@v6
        with:
          context: .
          platforms: ${{ github.event_name == 'pull_request' && 'linux/amd64' || 'linux/amd64,linux/arm64' }}
          push: ${{ github.event_name != 'pull_request' }}
          load: ${{ github.event_name == 'pull_request' }}
          tags: ${{ steps.meta.outputs.tags }}
          labels: ${{ steps.meta.outputs.labels }}
          build-args: |
            VERSION=${{ steps.meta.outputs.version }}
            GIT_SHA=${{ github.sha }}
          cache-from: type=gha
          cache-to: type=gha,mode=max
          provenance: mode=max
          sbom: true

      - name: Smoke test (PR only)
        if: github.event_name == 'pull_request'
        run: ./.github/scripts/smoke-test-image.sh ${{ steps.meta.outputs.version }}   # §3.5

      - name: Attest provenance
        if: github.event_name != 'pull_request'
        uses: actions/attest-build-provenance@v2
        with:
          subject-name: ghcr.io/o-mutt/audioyotoshelf
          subject-digest: ${{ steps.build.outputs.digest }}
          push-to-registry: true

  dockerhub-readme:
    # Keeps the Docker Hub page in sync with docs/docker-hub.md on releases.
    if: startsWith(github.ref, 'refs/tags/v') && vars.DOCKERHUB_REPOSITORY != ''
    needs: image
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
      - uses: peter-evans/dockerhub-description@v4
        with:
          username: ${{ secrets.DOCKERHUB_USERNAME }}
          password: ${{ secrets.DOCKERHUB_TOKEN }}
          repository: ${{ vars.DOCKERHUB_REPOSITORY }}
          readme-filepath: docs/docker-hub.md
```

Action major versions follow what `ci.yml` already pins; Dependabot's existing `github-actions`
entry keeps them current. Pin to the latest majors available at implementation time.

### 3.5 PR smoke test

`.github/scripts/smoke-test-image.sh` runs the freshly loaded image against throwaway Postgres and
Redis containers **using only the flat variables from §5** (this is what proves the env contract),
then:

1. waits up to 90 s for `GET /health/ready` → 200 (EF migrations ran, Redis reachable);
2. checks `GET /` returns the SPA shell (`wwwroot` made it into the image);
3. checks `ffmpeg -version` succeeds inside the container;
4. checks the process runs as a non-root UID;
5. dumps `docker logs` on failure.

### 3.6 Optional (recommended, not blocking v1)

- **Vulnerability scan:** `aquasecurity/trivy-action` on the pushed digest, uploading SARIF to code
  scanning; report-only at first (`exit-code: 0`), fail on `CRITICAL` once the baseline is clean.
- **Dependabot for base images:** add a `docker` ecosystem entry for `/` so `node:22-alpine`,
  `dotnet/sdk:10.0` and `dotnet/aspnet:10.0` bumps arrive as PRs.
- **Weekly rebuild** (`schedule:`) of the latest release tag to pick up base-image security patches.

## 4. Dockerfile changes

### 4.1 Multi-arch without emulating the build

Building the .NET and Node stages under QEMU for arm64 is slow (often 20+ minutes) and flaky. Run the
build stages on the runner's native platform and cross-target:

```dockerfile
FROM --platform=$BUILDPLATFORM node:22-alpine AS frontend-build      # output is arch-independent
...
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build
ARG TARGETARCH
RUN dotnet restore src/AudioYotoShelf.Api/AudioYotoShelf.Api.csproj -a $TARGETARCH
...
RUN dotnet publish ... -a $TARGETARCH --no-restore
```

Only the runtime stage (`apt-get install ffmpeg curl`) runs under emulation for arm64, which takes
seconds. `TARGETARCH` values (`amd64`, `arm64`) map onto .NET RIDs `linux-x64` / `linux-arm64`
through `-a`. The publish stays framework-dependent (no `--self-contained`).

### 4.2 Version and metadata

```dockerfile
ARG VERSION=0.0.0-local
ARG GIT_SHA=unknown
RUN dotnet publish ... -p:Version=${VERSION%%-*} -p:InformationalVersion=${VERSION}+${GIT_SHA}
```

In the runtime stage, `LABEL org.opencontainers.image.*` comes from `metadata-action` (source,
revision, version, created, licenses=MIT); add a static `LABEL org.opencontainers.image.source`
so locally built images still link back to the repo. Optionally surface `InformationalVersion` in
the startup log line and `/api/health`.

### 4.3 Runtime stage tidy-ups

- Replace `ENV ASPNETCORE_URLS=http://+:8080` with the .NET 8+ convention `ASPNETCORE_HTTP_PORTS=8080`
  (the base image already sets it; setting `ASPNETCORE_URLS` too logs an "overriding" warning).
- `HEALTHCHECK` stays on `/health/live` but reads the port so a changed `ASPNETCORE_HTTP_PORTS`
  doesn't mark the container unhealthy:
  `CMD curl -fsS "http://localhost:${ASPNETCORE_HTTP_PORTS:-8080}/health/live" || exit 1`.
- `VOLUME ["/app/temp"]` so the scratch audio area is never written into the container layer.
- No `ENTRYPOINT` shell wrapper: env handling lives in the app (§5.2), so the process stays PID 1
  and receives `SIGTERM` directly for graceful Hangfire shutdown.

## 5. Runtime configuration contract

### 5.1 Variables

Every row is settable at container start. "Native key" is the ASP.NET form, which keeps working and
**takes precedence** over the flat alias when both are set (so existing deployments and power users
are unaffected).

**Required**

| Variable | Native key | Default | Notes |
|----------|------------|---------|-------|
| `YOTO_CLIENT_ID` | `Yoto__ClientId` | — | Yoto OAuth app client ID. Without it, the Yoto connect step throws. |
| `DB_PASSWORD` | `ConnectionStrings__Postgres` | — | Postgres password; see the `DB_*` group below. |

**Database and cache**

| Variable | Native key | Default | Notes |
|----------|------------|---------|-------|
| `DB_HOST` | — | `postgres` | Composed into `ConnectionStrings:Postgres` when any `DB_*` variable is set |
| `DB_PORT` | — | `5432` | |
| `DB_NAME` | — | `audioyotoshelf` | |
| `DB_USER` | — | `bridge` | |
| `DB_PASSWORD` | — | — | Required whenever the connection string is composed |
| `DB_CONNECTION_STRING` | `ConnectionStrings__Postgres` | — | Full Npgsql connection string; when set, the `DB_*` parts are ignored |
| `REDIS_CONNECTION_STRING` | `ConnectionStrings__Redis` | `redis:6379` | StackExchange.Redis format, e.g. `redis:6379,password=secret` |

Defaults match the service names in `docker-compose.yml`, so the stock Compose file needs only
`DB_PASSWORD`. The connection string is built with `NpgsqlConnectionStringBuilder`, so a password
containing `;` or `=` is escaped correctly (the current Compose string interpolation breaks on those).

**Integrations**

| Variable | Native key | Default | Notes |
|----------|------------|---------|-------|
| `YOTO_CLIENT_SECRET` | `Yoto__ClientSecret` | — | Sent on token calls when set; required unless the Yoto app is a public PKCE client |
| `GEMINI_API_KEY` | `Gemini__ApiKey` | — | Optional; without it icons fall back to Yoto's public library |
| `GEMINI_MODEL` | `Gemini__Model` | `gemini-3.1-flash-image` | |
| `AUDIOBOOKSHELF_URL` | `Audiobookshelf__Url` | — | Pins the one ABS server; validated at boot (bad URL = fail fast) |
| `AUDIOBOOKSHELF_PUBLIC_URL` | `Audiobookshelf__PublicUrl` | — | Browser-facing ABS address for SSO when `AUDIOBOOKSHELF_URL` is internal |
| `ADMIN_AUDIOBOOKSHELF_URL` | `Admin__AudiobookshelfUrl` | — | Trusted ABS server that may grant admin |
| `ADMIN_USERNAMES` | `Admin__Usernames` | — | Comma-separated. Replaces the ad-hoc fallback read in `AuthController`. |

**Hosting and operations**

| Variable | Native key | Default | Notes |
|----------|------------|---------|-------|
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | (built-in .NET) | `false` | Set `true` behind a reverse proxy so `X-Forwarded-Proto`/`-For` are honoured and OAuth callbacks are built as `https://…`. It does not read `X-Forwarded-Host`, so the proxy must pass the original `Host` header through (Caddy/Traefik do by default; nginx needs `proxy_set_header Host $host`). Only enable when a proxy is actually in front: it trusts those headers from any client. |
| `ASPNETCORE_HTTP_PORTS` | (built-in .NET) | `8080` | Internal listen port. Normally leave it and map a host port instead. |
| `ASPNETCORE_ENVIRONMENT` | (built-in .NET) | `Production` | |
| `LOG_LEVEL` | `Serilog__MinimumLevel__Default` | `Information` | `Verbose`/`Debug`/`Information`/`Warning`/`Error` |
| `SEQ_URL` | `Serilog__SeqUrl` | — | Also ship logs to Seq |
| `TRANSFER_TEMP_DIR` | `Transfer__TempDirectory` | `/app/temp` | Scratch space for downloaded/split audio; must be writable by the container user. Keep `TMPDIR` pointing at the same volume. |

**Advanced (native keys only, documented but no alias)**

`Yoto__ApiBase`, `Yoto__AuthBase`, and `Yoto__CardLimits__{MaxTracks,MaxTrackBytes,MaxTrackDurationSeconds,MaxTotalBytes,MaxTotalDurationSeconds}`.
These exist for testing against a non-production Yoto or for when Yoto changes card limits; they
don't need friendly names.

**Secrets from files.** Each secret also accepts a `_FILE` variant that names a file to read the
value from, for Docker/Compose/Swarm secrets and Kubernetes secret mounts: `YOTO_CLIENT_SECRET_FILE`,
`DB_PASSWORD_FILE`, `DB_CONNECTION_STRING_FILE`, `REDIS_CONNECTION_STRING_FILE`, `GEMINI_API_KEY_FILE`.
Setting both `X` and `X_FILE` is a startup error. Trailing newlines are trimmed.

**Not exposed (cleanup):** `appsettings.json` defines `Gemini:MaxDailyRequests`,
`Transfer:MaxConcurrentTransfers`, `Transfer:TranscodePollIntervalMs` and
`Transfer:TranscodePollMaxAttempts`, but nothing reads them (the daily cap is the `DailyLimit`
constant, the Hangfire worker count is hard-coded to 2, and transcode polling uses constants in
`YotoService`). Either wire them up or delete them as part of this work; they must not be documented
as settings while they do nothing. Wiring `Transfer:MaxConcurrentTransfers` to the Hangfire
`WorkerCount` (as `TRANSFER_WORKERS`) is a reasonable follow-up.

### 5.2 Implementation: env alias configuration source

Add a small configuration source in `AudioYotoShelf.Api` (e.g. `Configuration/EnvironmentAliases.cs`)
and register it in `Program.cs` immediately **before** the default environment-variable source, so
precedence becomes:

`appsettings.json` < `appsettings.{env}.json` < **flat aliases** < native env vars (`Section__Key`) < command line

It:

1. Maps each flat variable in §5.1 to its configuration key (a static table, one line per variable).
2. Resolves `_FILE` variants, failing startup with a clear message if a file is missing or both
   forms are set.
3. Composes `ConnectionStrings:Postgres` from `DB_*` when any of them is set (and
   `DB_CONNECTION_STRING` is not), failing startup if `DB_PASSWORD` is missing in that case.
4. Never logs values. A single startup line lists which aliases were applied by **name** only.

Keeping this in .NET (not a shell entrypoint) means it works identically in Docker, under
`dotnet run`, and in the integration tests, and it is unit-testable.

Also add a startup check: if `Yoto:ClientId` is empty, log a prominent warning at boot (not a
crash — users must be able to reach the UI and health endpoints to diagnose).

### 5.3 Tests

- Unit tests for the alias source: each mapping, native-key precedence, `_FILE` handling (present,
  missing, both-set), connection-string composition including special characters in the password.
- The PR smoke test (§3.5) is the end-to-end check that the published image honours the contract.

## 6. Repo changes that ship with this

| File | Change |
|------|--------|
| `.github/workflows/docker.yml` | New workflow (§3) |
| `.github/scripts/smoke-test-image.sh` | New smoke test (§3.5) |
| `.github/dependabot.yml` | Add `docker` ecosystem (§3.6) |
| `Dockerfile` | `$BUILDPLATFORM` cross-build, version args, labels, port/healthcheck/volume tidy-ups (§4) |
| `src/AudioYotoShelf.Api/Configuration/EnvironmentAliases.cs` + `Program.cs` | Alias source (§5.2) |
| `src/AudioYotoShelf.Api/Controllers/AuthController.cs` | Drop the `ADMIN_USERNAMES` fallback read |
| `src/AudioYotoShelf.Api/appsettings.json` | Remove or wire the dead keys (§5.1) |
| `docker-compose.yml` | Add `image: ghcr.io/o-mutt/audioyotoshelf:${AYS_TAG:-latest}` next to `build: .`; replace the hand-built `ConnectionStrings__*`/`Section__Key` mappings with `env_file: .env` and the flat names |
| `.env.example` | Add the new optional variables (`LOG_LEVEL`, `SEQ_URL`, `ASPNETCORE_FORWARDEDHEADERS_ENABLED`, `AYS_TAG`, …) |
| `README.md` | "Run the published image" section (`docker run` and Compose examples), full variable table from §5.1, reverse-proxy note, tag policy (`latest` vs `edge` vs pinned) |
| `docs/docker-hub.md` | Short Docker Hub overview synced by the `dockerhub-readme` job |
| `restart.sh` | Offer `docker compose pull` as the update path when using the published image |

## 7. Rollout

1. Land the alias source + Dockerfile changes with tests (behaviour-preserving for current Compose
   users, because native keys still win).
2. Land `docker.yml` with Docker Hub left unconfigured; confirm `edge` appears on GHCR, flip the
   package to public, `docker pull` it on amd64 and arm64.
3. Add the Docker Hub secrets/variable; confirm the next `main` push reaches Docker Hub.
4. Tag `v0.1.0` (or whatever the first version should be) to produce `latest`, then update the README
   quick start to pull instead of build.

## 8. Open questions

1. **Docker Hub namespace** — which account/org owns `<namespace>/audioyotoshelf`?
2. **arm64** — keep it in v1? It costs little with the cross-build in §4.1; dropping it simplifies
   nothing material.
3. **First version number** — `v0.1.0`, or `v1.0.0` now that the feature set is in daily use?
4. **Hangfire dashboard** — `/hangfire` is anonymous and can expose job arguments. That is tolerable
   for a self-built LAN deploy; a public image lowers the bar to exposing it. Ship a
   `HANGFIRE_DASHBOARD_ENABLED` switch (default on, or off?) with this work, or as a follow-up?
