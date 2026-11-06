#!/usr/bin/env bash
# Accept only a short-lived repository registration token on stdin, never a PAT.
set -euo pipefail
umask 077
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"

if [[ -t 0 ]]; then
  echo 'Pipe a fresh runner-registration token into this script on stdin.' >&2
  exit 2
fi

token_file="$(mktemp "${TMPDIR:-/tmp}/valio-runner-token.XXXXXXXX")"
trap 'rm -f -- "${token_file}"' EXIT
cat > "${token_file}"
if [[ ! -s "${token_file}" ]]; then
  echo 'Registration token is empty.' >&2
  exit 2
fi

docker compose build runner
docker compose run --rm --no-deps \
  --volume "${token_file}:/run/secrets/runner-registration:ro" runner register
docker compose up --detach runner
