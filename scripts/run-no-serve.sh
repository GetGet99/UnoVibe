#!/usr/bin/env bash

nohup dotnet run --project UnoVibe/UnoVibe.csproj --framework net10.0-desktop \
    >/dev/null 2>&1 &