# syntax=docker/dockerfile:1.7

# Upstream runtime images with published apps
FROM ghcr.io/gml-launcher/gml.web.api:master AS upstream_api
FROM ghcr.io/gml-launcher/gml.web.proxy:master AS upstream_proxy
FROM ghcr.io/gml-launcher/gml.web.client:master AS upstream_client
FROM ghcr.io/gml-launcher/gml.web.skin.service:master AS upstream_skins

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS gml-core-build
RUN apt-get update && apt-get install -y --no-install-recommends git \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /src
# GML_CORE_SHA is referenced here on purpose: without it the clone layer stays in
# the buildx cache and a rebuild silently ships the previous Gml.Core commit.
ARG GML_CORE_SHA=master
RUN git clone --depth 1 https://github.com/ZinovevEzCode/Gml.Core.git . \
 && head="$(git rev-parse HEAD)" \
 && echo "Gml.Core at $head (expected $GML_CORE_SHA)" \
 && { [ "$GML_CORE_SHA" = "master" ] || [ "$head" = "$GML_CORE_SHA" ]; }
COPY patches/Directory.Build.props Directory.Build.props
COPY patches/SystemProcedures.cs src/Gml.Core/Core/Helpers/System/SystemProcedures.cs
COPY patches/MirrorsHelper.cs src/Gml.Core/Core/Helpers/Mirrors/MirrorsHelper.cs
COPY patches/ProfileProcedures.cs src/Gml.Core/Core/Helpers/Profiles/ProfileProcedures.cs
COPY patches/GameDownloader.cs src/Gml.Core/Core/Helpers/Game/GameDownloader.cs
COPY patches/LauncherProcedures.cs src/Gml.Core/Core/Helpers/Launcher/LauncherProcedures.cs
COPY patches/Gml.Core.csproj src/Gml.Core/Gml.Core.csproj
RUN dotnet publish src/Gml.Core/Gml.Core.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS economy-build
WORKDIR /src
COPY economy/ ./
RUN dotnet publish Andline.Economy.csproj -c Release -o /out --nologo

# Need both .NET 10 (API) and .NET 8 (Proxy / Skin / Economy) runtimes
# .NET 10 images are Ubuntu 24.04 (noble); Debian bookworm tags do not exist.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS dotnet8
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
       ca-certificates \
       curl \
       git \
       tzdata \
       supervisor \
       nodejs \
       iputils-ping \
       unzip \
       tar \
    && rm -rf /var/lib/apt/lists/*

# Merge .NET 8 runtimes into the .NET 10 image.
# COPY .../8.* flattens the version folder, so the host only sees 10.0.x.
RUN --mount=from=dotnet8,source=/usr/share/dotnet,target=/mnt/dotnet8,ro \
    mkdir -p /usr/share/dotnet/shared/Microsoft.NETCore.App \
             /usr/share/dotnet/shared/Microsoft.AspNetCore.App \
             /usr/share/dotnet/host/fxr \
 && cp -a /mnt/dotnet8/shared/Microsoft.NETCore.App/8.* /usr/share/dotnet/shared/Microsoft.NETCore.App/ \
 && cp -a /mnt/dotnet8/shared/Microsoft.AspNetCore.App/8.* /usr/share/dotnet/shared/Microsoft.AspNetCore.App/ \
 && cp -a /mnt/dotnet8/host/fxr/8.* /usr/share/dotnet/host/fxr/ \
 && ls -d /usr/share/dotnet/shared/Microsoft.NETCore.App/8.* \
 && ls -d /usr/share/dotnet/shared/Microsoft.AspNetCore.App/8.*

# Pterodactyl-compatible user and workspace
RUN useradd -m -d /home/container -s /bin/bash container \
    && mkdir -p /home/container /opt/gml /var/log/supervisor \
    && chown -R container:container /home/container /opt/gml /var/log/supervisor

# Copy upstream apps
COPY --from=upstream_api /app /opt/gml/api
# Overlay patched Gml.Core + CmlLib. Do NOT copy Microsoft/SQLite DLLs:
# that overwrites EF's Microsoft.Data.Sqlite and breaks SQLitePCLRaw.core.
RUN --mount=from=gml-core-build,source=/out,target=/mnt/core,ro \
    set -e \
 && for f in /mnt/core/*.dll; do \
      base="$(basename "$f")"; \
      case "$base" in \
        Microsoft.*|System.*|SQLite*|EntityFramework*|Azure.*|HarfBuzz*|SkiaSharp*) continue ;; \
      esac; \
      cp -a "$f" /opt/gml/api/; \
    done
COPY --from=upstream_proxy /app /opt/gml/proxy
COPY --from=upstream_client /app /opt/gml/client
COPY --from=upstream_skins /app /opt/gml/skins
COPY --from=economy-build /out /opt/gml/economy

# Local orchestration/config
COPY --chown=container:container entrypoint.sh /opt/gml/entrypoint.sh
COPY supervisord.conf /etc/supervisor/supervisord.conf
COPY supervisor-gml.conf /etc/supervisor/conf.d/gml.conf
COPY --chown=container:container proxy.appsettings.json /opt/gml/proxy/appsettings.json
COPY --chown=container:container proxy.appsettings.split.json /opt/gml/proxy/appsettings.split.json

# Linux JDK 22 for GML CheckBuildJava. Wings drops NET_RAW so ICMP mirrors
# never work; having java on disk skips GetAvailableMirrorAsync entirely.
RUN mkdir -p /opt/gml/jdk-22 \
 && (curl -fL --retry 3 --retry-delay 2 --connect-timeout 20 --max-time 900 \
       -o /tmp/jdk22.tgz \
       "https://api.adoptium.net/v3/binary/latest/22/ga/linux/x64/jdk/hotspot/normal/eclipse" \
     || curl -fL --retry 3 --retry-delay 2 --connect-timeout 20 --max-time 900 \
       -o /tmp/jdk22.tgz \
       "https://download.java.net/java/GA/jdk22.0.2/c9ecb94cd31b495da20a27d4581645e8/9/GPL/openjdk-22.0.2_linux-x64_bin.tar.gz") \
 && mkdir -p /tmp/jdk-extract \
 && tar -xzf /tmp/jdk22.tgz -C /tmp/jdk-extract \
 && inner="$(find /tmp/jdk-extract -mindepth 1 -maxdepth 1 -type d | head -n 1)" \
 && mkdir -p /opt/gml/jdk-22/jdk-22 \
 && cp -a "$inner"/. /opt/gml/jdk-22/jdk-22/ \
 && rm -rf /tmp/jdk22.tgz /tmp/jdk-extract \
 && test -x /opt/gml/jdk-22/jdk-22/bin/java

# .NET 8 SDK for InstallDotnet / launcher compile. Same Wings ICMP issue:
# "Get active mirrors..." never finishes if Ping hangs. Pre-install skips that.
RUN mkdir -p /opt/gml/dotnet-8 \
 && (curl -fL --retry 3 --retry-delay 2 --connect-timeout 20 --max-time 900 \
       -o /tmp/dotnet8.tgz \
       "https://builds.dotnet.microsoft.com/dotnet/Sdk/8.0.415/dotnet-sdk-8.0.415-linux-x64.tar.gz" \
     || curl -fL --retry 3 --retry-delay 2 --connect-timeout 20 --max-time 900 \
       -o /tmp/dotnet8.tgz \
       "https://aka.ms/dotnet/8.0/sdk-linux-x64.tar.gz") \
 && tar -xzf /tmp/dotnet8.tgz -C /opt/gml/dotnet-8 \
 && rm -f /tmp/dotnet8.tgz \
 && test -x /opt/gml/dotnet-8/dotnet

# Pangolin site connector (fosrl/newt). Userspace WireGuard; needs NEWT_* env.
RUN curl -fL --retry 3 --retry-delay 2 --connect-timeout 20 --max-time 120 \
      -o /usr/local/bin/newt \
      "https://github.com/fosrl/newt/releases/latest/download/newt_linux_amd64" \
 && chmod +x /usr/local/bin/newt

COPY --chown=container:container newt-wrapper.sh /opt/gml/newt-wrapper.sh

ARG IMAGE_REVISION=dev
RUN echo "$IMAGE_REVISION" > /opt/gml/.build-id \
 && chmod +x /opt/gml/entrypoint.sh /opt/gml/newt-wrapper.sh \
 && chown -R container:container /opt/gml

ENV HOME=/home/container \
    USER=container \
    ASPNETCORE_ENVIRONMENT=Production \
    TZ=Europe/Moscow \
    PUBLIC_PANEL_PORT=8080

WORKDIR /home/container
USER container

EXPOSE 8080
ENTRYPOINT ["/opt/gml/entrypoint.sh"]
