#!/usr/bin/env bash
set -euo pipefail

export HOME=/home/container
export PROJECT_NAME="${PROJECT_NAME:-GmlBackendPanel}"
export PROJECT_DESCRIPTION="${PROJECT_DESCRIPTION:-}"
export PROJECT_POLICYNAME="${PROJECT_POLICYNAME:-GmlServerPolicy}"
export PROJECT_PATH="${PROJECT_PATH:-/home/container/data/GmlBackend}"
export SWAGGER_ENABLED="${SWAGGER_ENABLED:-false}"
export MARKET_ENDPOINT="${MARKET_ENDPOINT:-https://gml-market.recloud.tech}"
export TZ="${TZ:-Europe/Moscow}"
export SERVICE_TEXTURE_ENDPOINT="${SERVICE_TEXTURE_ENDPOINT:-http://127.0.0.1:8085}"
export PUBLIC_PANEL_PORT="${PUBLIC_PANEL_PORT:-8080}"

# Required secret
if [[ -z "${SECURITY_KEY:-}" ]]; then
  echo "[entrypoint] ERROR: SECURITY_KEY is required."
  exit 1
fi

# Prepare persistent layout in /home/container
mkdir -p /home/container/data/GmlBackend
mkdir -p /home/container/data/backups
mkdir -p /home/container/data/TextureService
mkdir -p /home/container/data/database

# Keep a stable backups path expected by some code paths
if [[ ! -L /home/container/data/GmlBackend/backups && ! -e /home/container/data/GmlBackend/backups ]]; then
  ln -s /home/container/data/backups /home/container/data/GmlBackend/backups
fi

# API default SQLite location is relative: database/data.db
# Make it persistent by linking app-local "database" to /home/container/data/database.
rm -rf /opt/gml/api/database
ln -s /home/container/data/database /opt/gml/api/database

# Skin service persistent storage
rm -rf /opt/gml/skins/Storage
ln -s /home/container/data/TextureService /opt/gml/skins/Storage

# If app code still checks root-like project folder, provide a compatibility path under /home/container.
mkdir -p /home/container/.compat-root
rm -rf "/home/container/.compat-root/${PROJECT_NAME}"
ln -s /home/container/data/GmlBackend "/home/container/.compat-root/${PROJECT_NAME}"

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

# Harmless if Wings blocks it; works only with CAP_NET_ADMIN/sysctl.
echo "0 2147483647" >/proc/sys/net/ipv4/ping_group_range 2>/dev/null || true

echo "[entrypoint] Starting GML stack via supervisord..."
exec /usr/bin/supervisord -c /etc/supervisor/supervisord.conf -n
