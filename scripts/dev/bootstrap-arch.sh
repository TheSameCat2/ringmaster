#!/usr/bin/env bash

set -euo pipefail

required_commands=(dotnet git codex)
optional_commands=(gh)
missing_required=()
missing_optional=()

for command_name in "${required_commands[@]}"; do
    if ! command -v "${command_name}" >/dev/null 2>&1; then
        missing_required+=("${command_name}")
    fi
done

for command_name in "${optional_commands[@]}"; do
    if ! command -v "${command_name}" >/dev/null 2>&1; then
        missing_optional+=("${command_name}")
    fi
done

if ((${#missing_required[@]} > 0)); then
    printf 'Missing required commands: %s\n' "${missing_required[*]}" >&2
    printf 'Install the missing tools on Arch Linux before continuing.\n' >&2
    exit 1
fi

if ((${#missing_optional[@]} > 0)); then
    printf 'Optional commands not found: %s\n' "${missing_optional[*]}"
fi

dotnet restore Ringmaster.sln
