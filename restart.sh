#!/bin/bash

yes | docker image prune

echo "Rebuilding AudioYotoShelf"

docker compose build --no-cache
docker compose down

docker compose up -d --force-recreate

echo "Done!"
