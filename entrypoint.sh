#!/usr/bin/env bash
set -euo pipefail

export HOME=/home/container
export USER="${USER:-container}"
export TMPDIR="${TMPDIR:-/home/container/tmp}"
export PROJECT_NAME="${PROJECT_NAME:-GmlBackendPanel}"
export PROJECT_DESCRIPTION="${PROJECT_DESCRIPTION:-}"
export PROJECT_POLICYNAME="${PROJECT_POLICYNAME:-GmlServerPolicy}"
export PROJECT_PATH="${PROJECT_PATH:-/home/container/data/GmlBackend}"
export SWAGGER_ENABLED="${SWAGGER_ENABLED:-false}"
export MARKET_ENDPOINT="${MARKET_ENDPOINT:-https://gml-market.recloud.tech}"
export TZ="${TZ:-Europe/Moscow}"
export SERVICE_TEXTURE_ENDPOINT="${SERVICE_TEXTURE_ENDPOINT:-http://127.0.0.1:8085}"
export PUBLIC_PANEL_PORT="${PUBLIC_PANEL_PORT:-8080}"
export PUBLIC_PANEL_HOST="${PUBLIC_PANEL_HOST:-}"
export PUBLIC_API_HOST="${PUBLIC_API_HOST:-}"
export AUTH_TRUST_HOST=true
export PANGOLIN_ENDPOINT="${PANGOLIN_ENDPOINT:-}"
export NEWT_ID="${NEWT_ID:-}"
export NEWT_SECRET="${NEWT_SECRET:-}"
export SHOP_INTERNAL_URL="${SHOP_INTERNAL_URL:-}"
export SHOP_INTERNAL_KEY="${SHOP_INTERNAL_KEY:-}"
export SHOP_INTERNAL_HEADER="${SHOP_INTERNAL_HEADER:-X-Internal-Key}"

# Required secret
if [[ -z "${SECURITY_KEY:-}" ]]; then
  echo "[entrypoint] ERROR: SECURITY_KEY is required."
  exit 1
fi

# Wings mounts the image (including /opt/gml) read-only. Only /home/container is writable.
mkdir -p /home/container/tmp
mkdir -p /home/container/data/GmlBackend
mkdir -p /home/container/data/backups
mkdir -p /home/container/data/TextureService
mkdir -p /home/container/data/database
mkdir -p /home/container/data/plugins
mkdir -p /home/container/gml
mkdir -p /home/container/bin

# PluginsService uses Environment.ProcessPath, which is /usr/share/dotnet/dotnet
# when launched via /usr/bin/dotnet. That makes plugins dir /usr/share/dotnet/plugins
# (read-only). Run a muxer copy from the volume so plugins land on /home/container.
DOTNET_MUXER="$(readlink -f /usr/bin/dotnet 2>/dev/null || echo /usr/share/dotnet/dotnet)"
cp -f "$DOTNET_MUXER" /home/container/bin/dotnet
chmod +x /home/container/bin/dotnet
# Muxer looks next to itself first; DOTNET_ROOT is ignored until host/fxr exists.
ln -sfn /usr/share/dotnet/host /home/container/bin/host
ln -sfn /usr/share/dotnet/shared /home/container/bin/shared
rm -rf /home/container/bin/plugins
ln -sfn /home/container/data/plugins /home/container/bin/plugins

# Keep a stable backups path expected by some code paths
if [[ ! -L /home/container/data/GmlBackend/backups && ! -e /home/container/data/GmlBackend/backups ]]; then
  ln -s /home/container/data/backups /home/container/data/GmlBackend/backups
fi

RUNTIME=/home/container/gml
IMAGE_ID_FILE=/opt/gml/.build-id
RUNTIME_ID_FILE="$RUNTIME/.build-id"

sync_apps() {
  echo "[entrypoint] Syncing GML apps into writable /home/container/gml (Pterodactyl read-only root)..."
  for app in api proxy client skins economy; do
    rm -rf "$RUNTIME/$app"
    mkdir -p "$RUNTIME/$app"
    cp -a "/opt/gml/$app/." "$RUNTIME/$app/"
  done
  cp -f "$IMAGE_ID_FILE" "$RUNTIME_ID_FILE"
  echo "[entrypoint] App sync complete."
}

if [[ ! -f "$IMAGE_ID_FILE" ]]; then
  echo "[entrypoint] WARNING: missing $IMAGE_ID_FILE, syncing apps anyway."
  echo "unknown" > "$RUNTIME_ID_FILE.tmp"
  IMAGE_ID_FILE="$RUNTIME_ID_FILE.tmp"
  sync_apps
elif [[ ! -f "$RUNTIME_ID_FILE" ]] || ! cmp -s "$IMAGE_ID_FILE" "$RUNTIME_ID_FILE"; then
  sync_apps
else
  echo "[entrypoint] GML apps already synced for this image."
fi

normalize_host() {
  local h="${1:-}"
  h="${h#https://}"
  h="${h#http://}"
  h="${h%%/*}"
  echo "$h"
}

panel_host_for_auth="$(normalize_host "${PUBLIC_PANEL_HOST:-}")"
if [[ -n "$panel_host_for_auth" ]]; then
  export AUTH_URL="https://${panel_host_for_auth}"
  export NEXTAUTH_URL="https://${panel_host_for_auth}"
  echo "[entrypoint] Dashboard public URL: $AUTH_URL"
else
  echo "[entrypoint] WARNING: PUBLIC_PANEL_HOST is empty. After session expiry the dashboard will redirect to http://localhost:8081/auth/signin."
fi

# Next.js middleware builds redirects from Host / X-Forwarded-*. Without these
# it uses HOSTNAME:PORT (127.0.0.1:8081) when the access token expires.
apply_proxy_placeholders() {
  local dest="$1"
  local panel_host="$2"
  local api_host="$3"
  if [[ ! -f "$dest" ]]; then
    return
  fi
  if [[ -z "$panel_host" ]]; then
    PROXY_DEST="$dest" node -e '
      const fs = require("fs");
      const path = process.env.PROXY_DEST;
      const data = JSON.parse(fs.readFileSync(path, "utf8"));
      const route = data?.ReverseProxy?.Routes?.["frontend-route"];
      if (route) route.Transforms = [{ RequestHeaderOriginalHost: "true" }];
      fs.writeFileSync(path, JSON.stringify(data, null, 2) + "\n");
    '
    return
  fi
  sed -i "s/__PANEL_HOST__/${panel_host}/g" "$dest"
  if [[ -n "$api_host" ]]; then
    sed -i "s/__API_HOST__/${api_host}/g" "$dest"
  fi
}

# Host-based split: dashboard on PUBLIC_PANEL_HOST, API/files/ws/skins on PUBLIC_API_HOST.
# Empty API host keeps path-only routing, but the panel host is still forced into
# forwarded headers so auth redirects stay on the public domain.
configure_proxy_hosts() {
  local panel_host api_host template dest
  panel_host="$(normalize_host "${PUBLIC_PANEL_HOST:-}")"
  api_host="$(normalize_host "${PUBLIC_API_HOST:-}")"
  dest="$RUNTIME/proxy/appsettings.json"
  mkdir -p "$RUNTIME/proxy"

  if [[ -n "$panel_host" && -n "$api_host" && "$panel_host" != "$api_host" ]]; then
    template="/opt/gml/proxy/appsettings.split.json"
    if [[ ! -f "$template" ]]; then
      echo "[entrypoint] WARNING: missing $template, keeping path routing."
      cp -f /opt/gml/proxy/appsettings.json "$dest"
      apply_proxy_placeholders "$dest" "$panel_host" "$api_host"
    else
      cp -f "$template" "$dest"
      apply_proxy_placeholders "$dest" "$panel_host" "$api_host"
      echo "[entrypoint] Proxy split: panel=$panel_host  api=$api_host"
    fi
    return
  fi

  if [[ -f /opt/gml/proxy/appsettings.json ]]; then
    cp -f /opt/gml/proxy/appsettings.json "$dest"
  fi

  if [[ -z "$panel_host" ]]; then
    apply_proxy_placeholders "$dest" "" ""
    echo "[entrypoint] Proxy: single-host mode (path routing). Set PUBLIC_PANEL_HOST or session redirects go to localhost:8081."
    return
  fi

  apply_proxy_placeholders "$dest" "$panel_host" "$api_host"
  if [[ -z "$api_host" || "$panel_host" == "$api_host" ]]; then
    echo "[entrypoint] Proxy: single-host mode, dashboard origin https://$panel_host"
  fi
}

configure_proxy_hosts

# Persist SQLite and skin files on the volume, not next to the DLL.
rm -rf "$RUNTIME/api/database"
ln -sfn /home/container/data/database "$RUNTIME/api/database"

rm -rf "$RUNTIME/skins/Storage"
ln -sfn /home/container/data/TextureService "$RUNTIME/skins/Storage"

# If app code still checks root-like project folder, provide a compatibility path under /home/container.
mkdir -p /home/container/.compat-root
rm -rf "/home/container/.compat-root/${PROJECT_NAME}"
ln -sfn /home/container/data/GmlBackend "/home/container/.compat-root/${PROJECT_NAME}"

# Wings always drops CAP_NET_RAW. .NET Ping then fails and GML reports
# "Нет доступных зеркал". Skip that by placing Linux JDK where CheckBuildJava looks:
# InstallationDirectory/temp/JavaBuild/jdk-22/jdk-22/bin/java
# InstallationDirectory = PROJECT_PATH + cleaned PROJECT_NAME
install_build_java() {
  local dest_root="$1"
  local java_bin="$dest_root/temp/JavaBuild/jdk-22/jdk-22/bin/java"
  mkdir -p "$dest_root/temp/JavaBuild"
  if [[ -x "$java_bin" ]]; then
    echo "[entrypoint] Build JDK already present: $java_bin"
    return
  fi
  rm -rf "$dest_root/temp/JavaBuild/jdk-22"
  cp -a /opt/gml/jdk-22 "$dest_root/temp/JavaBuild/jdk-22"
  chmod +x "$dest_root/temp/JavaBuild/jdk-22/jdk-22/bin/"* 2>/dev/null || true
  echo "[entrypoint] Installed build JDK: $java_bin"
}

install_build_dotnet() {
  local dest_root="$1"
  local sdk_bin="$dest_root/temp/DotnetBuild/dotnet-8/dotnet"
  mkdir -p "$dest_root/temp/DotnetBuild"
  if [[ -x "$sdk_bin" ]]; then
    echo "[entrypoint] Build SDK already present: $sdk_bin"
    return
  fi
  rm -rf "$dest_root/temp/DotnetBuild/dotnet-8"
  cp -a /opt/gml/dotnet-8 "$dest_root/temp/DotnetBuild/dotnet-8"
  chmod +x "$sdk_bin" 2>/dev/null || true
  echo "[entrypoint] Installed build SDK: $sdk_bin"
}

CLEAN_NAME="$(echo "$PROJECT_NAME" | tr -cd 'A-Za-z0-9_-')"
install_build_java "$PROJECT_PATH"
install_build_java "$PROJECT_PATH/$CLEAN_NAME"
install_build_java "$HOME/$CLEAN_NAME"
install_build_dotnet "$PROJECT_PATH"
install_build_dotnet "$PROJECT_PATH/$CLEAN_NAME"
install_build_dotnet "$HOME/$CLEAN_NAME"

# Wings blocks sysctl; ignore EROFS.
if [[ -w /proc/sys/net/ipv4/ping_group_range ]]; then
  echo "0 2147483647" >/proc/sys/net/ipv4/ping_group_range || true
fi

echo "[entrypoint] Starting GML stack via supervisord..."
export AUTH_URL="${AUTH_URL:-}"
export NEXTAUTH_URL="${NEXTAUTH_URL:-}"
exec /usr/bin/supervisord -c /etc/supervisor/supervisord.conf -n
