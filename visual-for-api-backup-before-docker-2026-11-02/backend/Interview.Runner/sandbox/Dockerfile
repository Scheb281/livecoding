FROM node:24-bookworm-slim AS node
RUN npm install --global typescript@5.9.3 @types/node@24
FROM golang:1.26-bookworm AS go
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble
RUN apt-get update && apt-get install --yes --no-install-recommends python3 g++ openjdk-21-jdk-headless && rm -rf /var/lib/apt/lists/*
COPY --from=node /usr/local/bin/node /usr/local/bin/node
COPY --from=node /usr/local/lib/node_modules /opt/node-global
COPY --from=go /usr/local/go /usr/local/go
ENV PATH="/usr/local/go/bin:${PATH}" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
COPY shim.py /opt/interview/shim.py
RUN chmod 0555 /opt/interview/shim.py
USER 65532:65532
WORKDIR /work
ENTRYPOINT ["python3", "/opt/interview/shim.py"]
