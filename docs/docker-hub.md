# AudioYotoShelf

Bridge your [Audiobookshelf](https://www.audiobookshelf.org/) library to [Yoto](https://www.yotoplay.com/)
Make Your Own (MYO) cards: browse your library, transfer books to cards with generated pixel-art
chapter icons, and map series to playlists.

Source, issues and full documentation: <https://github.com/O-Mutt/AudioYotoShelf>

## Tags

| Tag | Tracks |
|-----|--------|
| `latest` | The newest release |
| `X.Y.Z`, `X.Y`, `X` | A release line |
| `edge` | Every merge to `main` (unreleased) |
| `sha-<commit>` | One exact build |

Images are built for `linux/amd64` and `linux/arm64`. The same images are published to
`ghcr.io/o-mutt/audioyotoshelf`.

## Quick start

The app needs PostgreSQL and Redis. The repository's
[`docker-compose.yml`](https://github.com/O-Mutt/AudioYotoShelf/blob/main/docker-compose.yml) runs all
three; against servers you already run:

```bash
docker run -d --name audioyotoshelf -p 8080:8080 \
  -e YOTO_CLIENT_ID=… -e YOTO_CLIENT_SECRET=… \
  -e DB_HOST=db.internal -e DB_PASSWORD=… \
  -e REDIS_CONNECTION_STRING=cache.internal:6379 \
  -e AUDIOBOOKSHELF_URL=https://abs.example.com \
  -v audioyotoshelf-keys:/home/app/.aspnet/DataProtection-Keys \
  ghcr.io/o-mutt/audioyotoshelf:latest   # or this Docker Hub repository
```

Keep the `DataProtection-Keys` volume, or every container update signs everyone out.

## Configuration

Everything is set with environment variables. The essentials:

| Variable | Description |
|----------|-------------|
| `YOTO_CLIENT_ID`, `YOTO_CLIENT_SECRET` | Yoto developer app credentials |
| `DB_PASSWORD` (+ `DB_HOST`, `DB_PORT`, `DB_NAME`, `DB_USER`) | PostgreSQL; defaults match the Compose file |
| `REDIS_CONNECTION_STRING` | Redis, default `redis:6379` |
| `AUDIOBOOKSHELF_URL` | The one Audiobookshelf server users may connect to |
| `GEMINI_API_KEY` | Optional, for generated chapter icons |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | `true` behind an HTTPS reverse proxy |

Secrets also accept a `_FILE` variant (e.g. `DB_PASSWORD_FILE=/run/secrets/db_password`). See the
[full variable list](https://github.com/O-Mutt/AudioYotoShelf#environment-variables).

## Health and metrics

`/health/live` (liveness, used by the image's `HEALTHCHECK`), `/health/ready` (PostgreSQL + Redis
reachable), and `/metrics` (Prometheus). Restrict `/metrics` at your proxy.
