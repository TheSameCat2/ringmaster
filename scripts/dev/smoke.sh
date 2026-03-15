#!/usr/bin/env bash

set -euo pipefail

dotnet build Ringmaster.sln
dotnet test Ringmaster.sln
./src/Ringmaster.App/bin/Debug/net10.0/ringmaster --help
