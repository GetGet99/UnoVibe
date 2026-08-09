#!/usr/bin/env bash

# Launches the app in the background. Any arguments given to this script are
# forwarded to the app (a folder path or an http(s) server URL, plus optional
# --password). Example: scripts/run-no-serve.sh http://localhost:4196
nohup dotnet run --project UnoVibe/UnoVibe.csproj --framework net10.0-desktop -- "$@" \
    >/dev/null 2>&1 &
