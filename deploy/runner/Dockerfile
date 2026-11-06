FROM docker:29.7.2-cli@sha256:3f4743208d2338c934d7b8bcfbe1bb54c0b2355c510ad5e0f31c0c4a54bd704e AS docker-cli
FROM ghcr.io/actions/actions-runner:2.338.2@sha256:759a1d94a15b05941aba5196974ff43fe15b4cbba29b694b35cbc32a1166b232

# Only the client is needed; jobs use the dedicated host user's rootless daemon.
USER root
COPY --from=docker-cli /usr/local/bin/docker /usr/local/bin/docker
COPY --from=docker-cli /usr/local/libexec/docker/cli-plugins/docker-compose /usr/local/lib/docker/cli-plugins/docker-compose
COPY --chmod=755 entrypoint.sh /usr/local/bin/valio-runner
ENV RUNNER_ALLOW_RUNASROOT=1
ENTRYPOINT ["/usr/local/bin/valio-runner"]
CMD ["run"]
