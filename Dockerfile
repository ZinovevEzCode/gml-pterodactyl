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
RUN git clone --depth 1 https://github.com/ZinovevEzCode/Gml.Core.git .
COPY patches/SystemProcedures.cs src/Gml.Core/Core/Helpers/System/SystemProcedures.cs
COPY patches/MirrorsHelper.cs src/Gml.Core/Core/Helpers/Mirrors/MirrorsHelper.cs
COPY patches/Gml.Core.csproj src/Gml.Core/Gml.Core.csproj
RUN dotnet publish src/Gml.Core/Gml.Core.csproj -c Release -o /out

# Need both .NET 10 (API) and .NET 8 (Proxy/Skin) runtimes
# .NET 10 images are Ubuntu 24.04 (noble); Debian bookworm tags do not exist.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS dotnet8
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
       ca-certificates \
       curl \
       tzdata \
       supervisor \
       nodejs \
       iputils-ping \
       unzip \
       tar \
    && rm -rf /var/lib/apt/lists/*

# Add .NET 8 runtime folders into .NET 10 base to support both app generations
COPY --from=dotnet8 /usr/share/dotnet/host/fxr/8.* /usr/share/dotnet/host/fxr/
COPY --from=dotnet8 /usr/share/dotnet/shared/Microsoft.NETCore.App/8.* /usr/share/dotnet/shared/Microsoft.NETCore.App/
COPY --from=dotnet8 /usr/share/dotnet/shared/Microsoft.AspNetCore.App/8.* /usr/share/dotnet/shared/Microsoft.AspNetCore.App/

# Pterodactyl-compatible user and workspace
RUN useradd -m -d /home/container -s /bin/bash container \
    && mkdir -p /home/container /opt/gml /var/log/supervisor \
    && chown -R container:container /home/container /opt/gml /var/log/supervisor

# Copy upstream apps
COPY --from=upstream_api /app /opt/gml/api
COPY --from=gml-core-build /out/Gml.Core.dll /opt/gml/api/Gml.Core.dll
COPY --from=upstream_proxy /app /opt/gml/proxy
COPY --from=upstream_client /app /opt/gml/client
COPY --from=upstream_skins /app /opt/gml/skins

# Local orchestration/config
COPY --chown=container:container entrypoint.sh /opt/gml/entrypoint.sh
COPY supervisord.conf /etc/supervisor/supervisord.conf
COPY supervisor-gml.conf /etc/supervisor/conf.d/gml.conf
COPY --chown=container:container proxy.appsettings.json /opt/gml/proxy/appsettings.json

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

ARG IMAGE_REVISION=dev
RUN echo "$IMAGE_REVISION" > /opt/gml/.build-id \
 && chmod +x /opt/gml/entrypoint.sh \
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
