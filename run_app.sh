#!/usr/bin/env bash
# Start the local database, then launch the Dispatcher desktop app.
set -euo pipefail
cd "$(dirname "$0")"

# .NET 8 SDK installed in ~/.dotnet (the system dotnet is older).
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$PATH"

docker compose down 

# Start PostgreSQL in the background and wait until it is healthy.
docker compose up -d --wait db

# Builds if needed, then runs; returns when the app window is closed.
dotnet run --project Dispatcher.Desktop
