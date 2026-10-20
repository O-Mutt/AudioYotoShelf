# The build stages run on the builder's own platform ($BUILDPLATFORM) and cross-target the image's
# architecture, so a multi-arch build never runs npm or the .NET SDK under emulation. Only the
# runtime stage's apt-get runs as the target architecture.

# Stage 1: Build Vue 3 SPA (output is architecture-independent)
FROM --platform=$BUILDPLATFORM node:26-alpine AS frontend-build
WORKDIR /app/client
COPY src/AudioYotoShelf.ClientApp/package*.json ./
RUN npm clean-install

COPY src/AudioYotoShelf.ClientApp/ ./
RUN npm run build

# Stage 2: Build .NET 10
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build
ARG TARGETARCH
WORKDIR /src

# Restore dependencies (layer caching)
COPY Directory.Build.props Directory.Packages.props ./
COPY src/AudioYotoShelf.Core/AudioYotoShelf.Core.csproj src/AudioYotoShelf.Core/
COPY src/AudioYotoShelf.Infrastructure/AudioYotoShelf.Infrastructure.csproj src/AudioYotoShelf.Infrastructure/
COPY src/AudioYotoShelf.Api/AudioYotoShelf.Api.csproj src/AudioYotoShelf.Api/
RUN dotnet restore src/AudioYotoShelf.Api/AudioYotoShelf.Api.csproj -a $TARGETARCH

# Copy source and build. VERSION is a semver from the release tag (or 0.0.0-local); the numeric part
# becomes the assembly version and the full string plus commit is logged at startup.
ARG VERSION=0.0.0-local
ARG GIT_SHA=unknown
COPY src/ src/
COPY --from=frontend-build /app/client/../AudioYotoShelf.Api/wwwroot/ src/AudioYotoShelf.Api/wwwroot/
RUN dotnet publish src/AudioYotoShelf.Api/AudioYotoShelf.Api.csproj \
    -c Release \
    -a $TARGETARCH \
    --self-contained false \
    -o /app \
    --no-restore \
    -p:Version=${VERSION%%-*} \
    -p:InformationalVersion=${VERSION}+${GIT_SHA}

# Stage 3: Runtime with FFmpeg
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

LABEL org.opencontainers.image.title="AudioYotoShelf" \
      org.opencontainers.image.description="Bridge your Audiobookshelf library to Yoto MYO cards" \
      org.opencontainers.image.source="https://github.com/O-Mutt/AudioYotoShelf" \
      org.opencontainers.image.licenses="MIT"

# curl is for the HEALTHCHECK below; the aspnet base image does not include it.
RUN apt-get update && \
    apt-get install -y --no-install-recommends ffmpeg curl && \
    rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=backend-build /app ./

# /app/temp: scratch space for audio processing. DataProtection-Keys: ASP.NET's default key ring
# location for the app user; it encrypts the session cookie, so it must outlive the container or
# every image update signs everyone out. Created here so a named volume inherits the ownership.
RUN mkdir -p /app/temp /home/app/.aspnet/DataProtection-Keys && \
    chown $APP_UID:$APP_UID /app/temp && \
    chown -R $APP_UID:$APP_UID /home/app/.aspnet
VOLUME ["/app/temp", "/home/app/.aspnet/DataProtection-Keys"]

USER $APP_UID

# The base image already listens on ASPNETCORE_HTTP_PORTS=8080. Runtime settings are environment
# variables documented in the README (YOTO_CLIENT_ID, DB_PASSWORD, …).
EXPOSE 8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV TMPDIR=/app/temp

# Liveness only: /api/health also probes Postgres, Redis and ffmpeg, so a slow first boot (EF
# migrations against a cold database) would report unhealthy — and proxies skip unhealthy containers.
HEALTHCHECK --interval=30s --timeout=5s --retries=3 --start-period=60s \
    CMD curl -fsS "http://localhost:${ASPNETCORE_HTTP_PORTS:-8080}/health/live" || exit 1

ENTRYPOINT ["dotnet", "AudioYotoShelf.Api.dll"]
