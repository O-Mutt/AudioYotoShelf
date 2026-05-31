#!/bin/bash
set -euo pipefail

docker image prune -f

echo "Rebuilding AudioYotoShelf"

# Build BEFORE touching the running stack. With set -e, a failed build (e.g. a transient
# DNS error pulling the base image) aborts here instead of silently bringing the old image
# back up. Cached base layers are reused, so a successful prior pull survives a DNS blip.
docker compose build

docker compose down
docker compose up -d --force-recreate

echo "Done!"
