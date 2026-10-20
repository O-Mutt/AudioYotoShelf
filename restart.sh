#!/bin/bash
set -euo pipefail

# ./restart.sh         rebuild the image from this checkout and restart
# ./restart.sh --pull  pull the published image (AYS_TAG, default latest) and restart

docker image prune -f

if [ "${1:-}" = "--pull" ]; then
    echo "Pulling AudioYotoShelf"
    # Pull BEFORE touching the running stack, for the same reason as the build below.
    docker compose pull bridge
    docker compose down
    docker compose up -d --force-recreate --no-build
    echo "Done!"
    exit 0
fi

echo "Rebuilding AudioYotoShelf"

# Build BEFORE touching the running stack. With set -e, a failed build (e.g. a transient
# DNS error pulling the base image) aborts here instead of silently bringing the old image
# back up. Cached base layers are reused, so a successful prior pull survives a DNS blip.
docker compose build

docker compose down
docker compose up -d --force-recreate

echo "Done!"
