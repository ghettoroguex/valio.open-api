# Optional ValIO deployment runner

This Compose stack runs a repository-scoped GitHub Actions runner on Linux x64.
It is intended for the `valio-deploy` job only. Builds, tests and public npm
Trusted Publishing remain on GitHub-hosted runners. These files prepare the
installation; they do not mean a runner is already registered on `77.91.74.209`.

The image uses GitHub's official `actions/actions-runner:2.338.2` and Docker's
official CLI/Compose. Runner identity, job workspace and web deployment settings
use separate named volumes. There is no Docker-in-Docker, privileged mode,
long-lived GitHub PAT or rootful Docker socket. The runner container is limited
to one CPU and 1 GiB RAM. Containers it deploys have their own limits in
`deploy/web/compose.yml`.

## Prepare a separate host account and rootless Docker

Inspect capacity and the existing services first. The host already serves other
workloads; do not reuse the `bytalent-ci` account, its daemon, or its socket.
Create a dedicated `valio-ci` account with its own home and subordinate UID/GID
ranges. For Debian/Ubuntu, the host administrator can prepare the prerequisites:

```sh
sudo apt-get install -y uidmap dbus-user-session docker-ce-rootless-extras
sudo adduser --disabled-password --gecos '' valio-ci
sudo loginctl enable-linger valio-ci
```

Verify that `/etc/subuid` and `/etc/subgid` each allocate at least 65,536 unique
IDs to `valio-ci`. Follow [Docker's rootless setup instructions](https://docs.docker.com/engine/security/rootless/)
for the host distribution. As `valio-ci`, with its own valid systemd user session:

```sh
dockerd-rootless-setuptool.sh install
systemctl --user enable --now docker
export DOCKER_HOST="unix:///run/user/$(id -u)/docker.sock"
docker info --format '{{json .SecurityOptions}}'
docker compose version
```

The Docker security options must include `name=rootless`. Preserve all existing
Docker/RKE2 services; installing this separate daemon does not require disabling
another daemon. Configure a free Docker address pool if the default pools
conflict with RKE2 or existing networks. Confirm CPU/memory limits are enforced
under cgroup v2; rootless resource control depends on systemd delegation.

Copy this directory to `valio-ci`'s home. Set its `.env` from `.env.example`:

```sh
cp .env.example .env
chmod 600 .env
# Set VALIO_DOCKER_SOCKET to /run/user/<valio-ci-uid>/docker.sock.
docker compose config --quiet
```

The runner executes as UID 0 **inside the rootless user namespace** so it can
access its owner's socket. On the host that UID maps to `valio-ci`, not root.
The entrypoint refuses a Docker endpoint that does not report rootless mode.
Possession of this socket still controls all containers of `valio-ci`; dedicate
that account to ValIO and keep unrelated services elsewhere.

## Register without retaining a GitHub PAT

In `valio-projects/valio.open-api` open **Settings → Actions → Runners → New
self-hosted runner**, choose Linux x64, and obtain the one-hour registration
token. The repository URL is fixed in Compose. Do not paste the token into
committed files, shell history, `.env`, or workflow variables.

On the host, as `valio-ci` with its rootless Docker context active:

```sh
read -r -s -p 'One-hour runner registration token: ' registration_token
printf '\n'
printf '%s' "$registration_token" | bash register.sh
unset registration_token
docker compose logs --tail 30 runner
```

`register.sh` reads stdin into a mode-0600 temporary file, mounts it into a
one-off registration container, then deletes the file. The persistent service
does not receive that token. It keeps only the runner identity issued by GitHub,
which is necessary to reconnect after a restart. Never use a PAT in place of a
runner registration token.

Confirm GitHub shows this runner **Idle** with labels `self-hosted`, `Linux`,
`X64`, `valio-deploy`. The logs should report **Listening for Jobs**. Restrict
workflows using this runner to reviewed default-branch deployment jobs; public
pull requests must not target it. Do not set it as the general Linux CI runner.

## Enable deployment

Create the application `.env` inside the persistent deployment volume before
enabling the workflow. A secret-free example is available at
`deploy/web/.env.example`:

```sh
docker compose exec -T runner sh -c 'umask 077; cat > /deploy/valio-web/.env' \
  < ../web/.env.example
```

Update the copied values for the actual public host, proxy peer and available
resources. Configure repository variables:

| Variable | Value |
| --- | --- |
| `VALIO_WEB_DEPLOY_PATH` | `/deploy/valio-web` |
| `VALIO_WEB_URL` | Actual public HTTPS URL |
| `VALIO_WEB_DEPLOY_ENABLED` | `true` after routing and runner checks |

Only enable deployment after the dedicated daemon and public TLS proxy work.
The web port binds to that daemon's loopback by default. Rootless networking
may change the peer address seen by the application; inspect it before setting
`VALIO_TRUSTED_PROXY`. See `../web/README.md` for WebSocket proxying and rollback.

For a restart check, run `docker compose restart runner` and verify the same
runner returns to **Idle** without requesting another registration token.

## Update and remove

Automatic runner updates are disabled because the binary lives in the image.
Monitor [official runner releases](https://github.com/actions/runner/releases),
update the pinned image version, then rebuild and recreate the service:

```sh
docker compose build --pull runner
docker compose up -d --force-recreate runner
```

GitHub requires runner updates within 30 days of a new version. Registered
identity remains in `runner-state` across image changes. To stop this optional
service use `docker compose down` without `--volumes`; remove its registration
in GitHub separately when retiring it. Keep the web deployment and data
protection volumes until they are no longer needed.
