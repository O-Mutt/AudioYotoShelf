#!/usr/bin/env bash
# Smoke-tests a built image against throwaway Postgres and Redis containers, configured only through
# the flat variables the image documents (DB_PASSWORD, YOTO_CLIENT_ID, …). Postgres and Redis are
# reachable as `postgres` and `redis`, so the image's defaults for DB_HOST and Redis are exercised too.
#
#   .github/scripts/smoke-test-image.sh <image>
set -euo pipefail

IMAGE=${1:?usage: smoke-test-image.sh <image>}
PREFIX=ays-smoke-$$
PORT=${SMOKE_PORT:-18080}
# Connection-string syntax in the password proves it is escaped, not interpolated.
DB_PASSWORD='smoke;pw=1'
BASE=http://127.0.0.1:$PORT

cleanup() {
  local status=$?
  if [ "$status" -ne 0 ]; then
    echo "--- app logs"
    docker logs "$PREFIX-app" 2>&1 | tail -100 || true
  fi
  docker rm -f "$PREFIX-app" "$PREFIX-db" "$PREFIX-redis" >/dev/null 2>&1 || true
  docker network rm "$PREFIX" >/dev/null 2>&1 || true
  exit "$status"
}
trap cleanup EXIT

fail() { echo "FAIL: $*" >&2; exit 1; }
pass() { echo "ok: $*"; }

docker network create "$PREFIX" >/dev/null
docker run -d --name "$PREFIX-db" --network "$PREFIX" --network-alias postgres \
  -e POSTGRES_DB=audioyotoshelf -e POSTGRES_USER=bridge -e POSTGRES_PASSWORD="$DB_PASSWORD" \
  postgres:17-alpine >/dev/null
docker run -d --name "$PREFIX-redis" --network "$PREFIX" --network-alias redis redis:7-alpine >/dev/null

# Over TCP: during first-boot init Postgres listens only on its socket, then restarts.
for _ in $(seq 60); do
  docker exec "$PREFIX-db" pg_isready -h 127.0.0.1 -U bridge -d audioyotoshelf >/dev/null 2>&1 && break
  sleep 1
done
docker exec "$PREFIX-db" pg_isready -h 127.0.0.1 -U bridge -d audioyotoshelf >/dev/null || fail "Postgres never became ready"

docker run -d --name "$PREFIX-app" --network "$PREFIX" -p "127.0.0.1:$PORT:8080" \
  -e DB_PASSWORD="$DB_PASSWORD" \
  -e YOTO_CLIENT_ID=smoke-client \
  "$IMAGE" >/dev/null

# Readiness = migrations ran and Postgres + Redis are reachable.
ready=
for _ in $(seq 90); do
  [ "$(docker inspect -f '{{.State.Running}}' "$PREFIX-app")" = true ] || fail "container exited"
  if curl -fsS "$BASE/health/ready" >/dev/null 2>&1; then ready=1; break; fi
  sleep 1
done
[ -n "$ready" ] || fail "/health/ready never returned 200"
pass "/health/ready"

curl -fsS "$BASE/" | grep -q '<div id="app"' || fail "/ did not serve the SPA shell"
pass "SPA shell served"

curl -fsS "$BASE/hangfire" | grep -q 'AudioYotoShelf Jobs' && fail "Hangfire dashboard is on by default"
pass "Hangfire dashboard off by default"

# The same command the image's HEALTHCHECK runs.
docker exec "$PREFIX-app" sh -c 'curl -fsS "http://localhost:${ASPNETCORE_HTTP_PORTS:-8080}/health/live"' >/dev/null \
  || fail "HEALTHCHECK command failed"
pass "HEALTHCHECK command"

docker exec "$PREFIX-app" ffmpeg -version >/dev/null || fail "ffmpeg missing"
pass "ffmpeg present"

[ "$(docker exec "$PREFIX-app" id -u)" != 0 ] || fail "running as root"
pass "non-root user"

logs=$(docker logs "$PREFIX-app" 2>&1)
grep -q 'Configuration read from environment variables: .*DB_PASSWORD' <<<"$logs" || fail "aliases not reported at startup"
grep -qF "$DB_PASSWORD" <<<"$logs" && fail "a secret value appeared in the logs"
pass "aliases applied, secrets not logged"

echo "Smoke test passed for $IMAGE"
