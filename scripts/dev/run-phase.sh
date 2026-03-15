#!/usr/bin/env bash

set -euo pipefail

if (($# != 1)); then
    echo "Usage: $0 <packet-id>" >&2
    exit 64
fi

packet_id="$1"

prompt=$(
    cat <<EOF
Read planning/PRODUCT.md and planning/IMPLEMENTATION.md first.
Then read AGENTS.md.

Implement packet ${packet_id} only.
Do not start later packets.
Keep changes minimal and production-quality.
Add or update tests.
Run the relevant tests.
Update planning/IMPLEMENTATION.md by checking off the packet if complete and append a short session log entry.
If blocked, stop and explain the blocker clearly.
EOF
)

codex exec \
    --cd . \
    --sandbox workspace-write \
    --ask-for-approval never \
    "${prompt}"
