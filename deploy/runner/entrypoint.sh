#!/usr/bin/env bash
set -euo pipefail
umask 077

cd /home/runner
state_files=(.runner .credentials .credentials_rsaparams .env .path)

restore_state() {
  for name in "${state_files[@]}"; do
    if [[ -f "/runner-state/${name}" ]]; then
      cp -- "/runner-state/${name}" "${name}"
    fi
  done
}

save_state() {
  for name in "${state_files[@]}"; do
    if [[ -f "${name}" ]]; then
      cp -- "${name}" "/runner-state/${name}"
      chmod 600 "/runner-state/${name}"
    fi
  done
}

require_rootless_docker() {
  # A bind-mounted rootful socket would grant host root despite cap_drop.
  if ! docker info --format '{{json .SecurityOptions}}' | grep -q 'name=rootless'; then
    echo 'Refusing to run: Docker endpoint must report rootless security mode.' >&2
    exit 1
  fi
}

restore_state
require_rootless_docker

case "${1:-run}" in
  register)
    : "${RUNNER_URL:?Set RUNNER_URL}"
    : "${RUNNER_NAME:?Set RUNNER_NAME}"
    if [[ "${RUNNER_URL}" != 'https://github.com/valio-projects/valio.open-api' ]]; then
      echo 'This deployment runner is restricted to valio-projects/valio.open-api.' >&2
      exit 1
    fi
    if [[ -f .runner ]]; then
      echo 'Runner is already configured; start it with docker compose up.' >&2
      exit 1
    fi
    if [[ ! -s /run/secrets/runner-registration ]]; then
      echo 'Supply a fresh repository runner-registration token via register.sh stdin.' >&2
      exit 1
    fi
    registration_token="$(cat /run/secrets/runner-registration)"
    ./config.sh --unattended --url "${RUNNER_URL}" --name "${RUNNER_NAME}" \
      --token "${registration_token}" --labels valio-deploy \
      --work _work --disableupdate
    unset registration_token
    save_state
    echo 'Registration saved. Start the runner with docker compose up -d.'
    ;;
  run)
    if [[ ! -f .runner ]]; then
      echo 'Runner is not registered. Run register.sh before starting the service.' >&2
      exit 1
    fi
    # exec lets Compose's init forward SIGTERM to the runner for graceful stop.
    exec ./run.sh
    ;;
  *)
    echo 'Supported commands: register, run.' >&2
    exit 2
    ;;
esac
