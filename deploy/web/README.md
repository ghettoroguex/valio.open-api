# Web application delivery

`src/ValIO.Web/Dockerfile` packages the Blazor Server UI and its REST API as one
.NET 10 image. The image runs as the built-in unprivileged `app` user and checks
`/healthz`. No separate JavaScript frontend build is required for this application.

## Local verification

From the repository root:

```sh
docker build -f src/ValIO.Web/Dockerfile -t valio-web:local .
VALIO_WEB_IMAGE=valio-web:local docker compose -f deploy/web/compose.yml up -d --wait
curl --fail http://127.0.0.1:18080/healthz
curl --fail http://127.0.0.1:18080/api/languages
```

The Compose port defaults to loopback `127.0.0.1:18080`. Data protection keys live
in a named Docker volume so a container replacement preserves Blazor antiforgery
and culture cookies. The application filesystem is read-only with a temporary
directory for generation, and default resource limits are two CPUs and 1 GiB RAM.

## GitHub Actions

The **Web application** workflow builds the container and starts it to verify the
health, language API, server-rendered homepage, schema validation and generated
C# ZIP. Pull requests and ordinary branches only validate. The Release workflow
calls this workflow after its full CI gate succeeds; default-branch builds
publish the tested image to
`ghcr.io/valio-projects/valio-web`, tagged with the commit and `latest`.
Deployments use the immutable digest reported in the workflow summary.

No separate GHCR token is required: the workflow uses `GITHUB_TOKEN` with
`packages:write` for publishing and `packages:read` for deployment. Set the
package visibility to public if unauthenticated users should pull it.

Automatic deployment is opt-in. Register a dedicated Linux x64 runner with the
`valio-deploy` label, Docker Compose v2 and access to its intended Docker context.
Rootless Docker is supported through the runner's `DOCKER_HOST`; this setup does
not require the root daemon's socket. Configure these repository variables:

| Variable | Value |
| --- | --- |
| `VALIO_WEB_DEPLOY_ENABLED` | `true` once the target host is ready |
| `VALIO_WEB_DEPLOY_PATH` | Persistent writable directory, default `$HOME/valio/web` |
| `VALIO_WEB_URL` | Public HTTPS URL, displayed in the deployment environment |

For a containerized runner, mount the deployment directory persistently and set
`VALIO_WEB_DEPLOY_PATH` to its path inside the runner. The application itself uses
only a named volume, so Compose does not need access to checkout files from the
Docker host. Keep untrusted pull-request jobs off the deployment runner.

Copy `.env.example` into `<deployment-directory>/.env` and configure it before
enabling deployment. The workflow copies `compose.yml`, preserves `.env`, pulls
the selected digest and waits up to 120 seconds for health. It records a successful
digest in `release.env` and restores the previous image if a later deployment
fails health checks. Docker images remain available for an explicit rollback:

```sh
export VALIO_WEB_IMAGE=ghcr.io/valio-projects/valio-web@sha256:PREVIOUS_DIGEST
export VALIO_WEB_DEPLOY_PATH=/persistent/valio/web
bash deploy/web/deploy.sh
```

## HTTPS reverse proxy

Keep the application on loopback and route the selected public hostname through
the server's existing TLS reverse proxy. Blazor Server requires WebSocket
upgrades and a sufficiently long idle timeout. For Nginx, the location block is:

```nginx
location / {
    proxy_pass http://127.0.0.1:18080;
    proxy_http_version 1.1;
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-For $remote_addr;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection "upgrade";
    proxy_read_timeout 3600s;
}
```

Set `VALIO_ALLOWED_HOSTS` to the public hostname (semicolon-separated for several
names). Set `VALIO_TRUSTED_PROXY` to the proxy's exact peer address as seen by
Kestrel. For a proxy on the Docker host, this is typically the Compose network's
gateway, which can be inspected with `docker network inspect valio-web_default`.
The application trusts forwarded scheme/address headers only from known proxies;
it does not enable blanket trust for arbitrary Internet clients. Configure the
proxy address before serving HTTPS to avoid redirect loops.

The public hostname, DNS and TLS proxy are deployment-specific and must be
configured on the host. This workflow does not replace an existing ingress or
claim a domain that has not been configured.
