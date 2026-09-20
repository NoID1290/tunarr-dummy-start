# Multi-arch Dockerfile for Tunarr Dummy Start (Linux ARM64, ARMv7, AMD64)
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG TARGETARCH
WORKDIR /src

# Copy project files and restore
COPY TunarrDummyStart/TunarrDummyStart.csproj TunarrDummyStart/
WORKDIR /src/TunarrDummyStart

# Map Docker TARGETARCH to .NET RuntimeIdentifier
RUN case "${TARGETARCH}" in \
        "arm64") RID="linux-arm64" ;; \
        "arm")   RID="linux-arm" ;; \
        "amd64") RID="linux-x64" ;; \
        *)       RID="linux-x64" ;; \
    esac && \
    dotnet restore -r $RID -f net8.0

# Copy all source files and publish
WORKDIR /src
COPY TunarrDummyStart/ TunarrDummyStart/
WORKDIR /src/TunarrDummyStart

RUN case "${TARGETARCH}" in \
        "arm64") RID="linux-arm64" ;; \
        "arm")   RID="linux-arm" ;; \
        "amd64") RID="linux-x64" ;; \
        *)       RID="linux-x64" ;; \
    esac && \
    dotnet publish -c Release -r $RID -f net8.0 --self-contained true -p:PublishSingleFile=true -o /app/publish

# Runtime image with FFmpeg installed
FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim AS final
RUN apt-get update && \
    apt-get install -y --no-install-recommends ffmpeg curl tzdata && \
    rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish/TunarrDummyStart .

# Create volume for persistent configuration
VOLUME ["/config"]
ENV TUNARR_CONFIG_PATH=/config/config.json

EXPOSE 1290

ENTRYPOINT ["./TunarrDummyStart", "--start"]

