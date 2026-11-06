#!/usr/bin/env bash
# Deploy a tested image. Requires an existing Docker context with Compose v2.
set -euo pipefail

: "${VALIO_WEB_IMAGE:?Set VALIO_WEB_IMAGE to the immutable image produced by CI}"
if [[ ! "${VALIO_WEB_IMAGE}" =~ ^ghcr\.io/[a-z0-9._/-]+@sha256:[a-f0-9]{64}$ ]]; then
  echo 'VALIO_WEB_IMAGE must be a ghcr.io image pinned by its sha256 digest.' >&2
  exit 1
fi

source_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
deploy_dir="${VALIO_WEB_DEPLOY_PATH:-${HOME}/valio/web}"
mkdir -p -- "${deploy_dir}"
deploy_dir="$(cd -- "${deploy_dir}" && pwd)"
if [[ "${source_dir}" != "${deploy_dir}" ]]; then
  cp -- "${source_dir}/compose.yml" "${deploy_dir}/compose.yml"
fi
cd -- "${deploy_dir}"

# Keep host settings separate from the image selected by the workflow. Existing
# .env files may configure the address, proxy, resources and allowed hostnames.
compose=(docker compose --project-name valio-web --file compose.yml)
previous_image=""
if [[ -f release.env ]]; then
  previous_image="$(sed -n 's/^VALIO_WEB_IMAGE=//p' release.env)"
fi
printf 'VALIO_WEB_IMAGE=%s\n' "${VALIO_WEB_IMAGE}" > release.env.new
chmod 600 release.env.new

"${compose[@]}" pull web
if "${compose[@]}" up --detach --wait --wait-timeout 120; then
  mv -- release.env.new release.env
  "${compose[@]}" ps
  printf 'Deployed %s\n' "${VALIO_WEB_IMAGE}"
else
  echo 'Deployment did not become healthy. Container logs follow.' >&2
  "${compose[@]}" logs --tail 100 web >&2 || true
  if [[ -n "${previous_image}" ]]; then
    export VALIO_WEB_IMAGE="${previous_image}"
    echo "Restoring previous image ${previous_image}." >&2
    "${compose[@]}" up --detach --wait --wait-timeout 120
  fi
  rm -f -- release.env.new
  exit 1
fi
