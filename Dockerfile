# syntax=docker/dockerfile:1.7

# Upstream runtime images with published apps
FROM ghcr.io/gml-launcher/gml.web.api:master AS upstream_api
FROM ghcr.io/gml-launcher/gml.web.proxy:master AS upstream_proxy
FROM ghcr.io/gml-launcher/gml.web.client:master AS upstream_client
FROM ghcr.io/gml-launcher/gml.web.skin.service:master AS upstream_skins

# Need both .NET 10 (API) and .NET 8 (Proxy/Skin) runtimes
FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim AS dotnet8
FROM mcr.microsoft.com/dotnet/aspnet:10.0-bookworm-slim AS runtime

ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
       ca-certificates \
       curl \
       tzdata \
       supervisor \
       nodejs \
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
COPY --from=upstream_proxy /app /opt/gml/proxy
COPY --from=upstream_client /app /opt/gml/client
COPY --from=upstream_skins /app /opt/gml/skins

# Local orchestration/config
COPY --chown=container:container entrypoint.sh /opt/gml/entrypoint.sh
COPY supervisord.conf /etc/supervisor/supervisord.conf
COPY supervisor-gml.conf /etc/supervisor/conf.d/gml.conf
COPY --chown=container:container proxy.appsettings.json /opt/gml/proxy/appsettings.json

RUN chmod +x /opt/gml/entrypoint.sh \
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
