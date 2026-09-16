#!/usr/bin/env bash
# Idempotent Cloud Agent setup for ActionStacksEX (Dalamud .NET 10 plugin).
# - Installs the .NET 10 SDK and exposes `dotnet` on the default PATH.
# - Fetches the matching Dalamud dev libraries to the SDK's default Linux path
#   (~/.xlcore/dalamud/Hooks/dev), so no DALAMUD_HOME env var is required.
# - Restores and builds the plugin in Release to prove the toolchain works.
set -euo pipefail

DOTNET_CHANNEL="10.0"
DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
DALAMUD_LIB_DIR="$HOME/.xlcore/dalamud/Hooks/dev"
DALAMUD_ZIP_URL="https://goatcorp.github.io/dalamud-distrib/latest.zip"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

# --- .NET 10 SDK ---------------------------------------------------------------
if ! "$DOTNET_ROOT/dotnet" --list-sdks 2>/dev/null | grep -q '^10\.'; then
  echo "[install] Installing .NET SDK channel ${DOTNET_CHANNEL} into ${DOTNET_ROOT}"
  tmp_script="$(mktemp)"
  curl -sSL https://dot.net/v1/dotnet-install.sh -o "$tmp_script"
  bash "$tmp_script" --channel "$DOTNET_CHANNEL" --install-dir "$DOTNET_ROOT"
  rm -f "$tmp_script"
else
  echo "[install] .NET 10 SDK already present in ${DOTNET_ROOT}"
fi

# Make `dotnet` discoverable on the default PATH without editing shell profiles.
if [ ! -e /usr/local/bin/dotnet ] || [ "$(readlink -f /usr/local/bin/dotnet 2>/dev/null)" != "$DOTNET_ROOT/dotnet" ]; then
  echo "[install] Linking ${DOTNET_ROOT}/dotnet -> /usr/local/bin/dotnet"
  sudo ln -sf "$DOTNET_ROOT/dotnet" /usr/local/bin/dotnet
fi
export PATH="$DOTNET_ROOT:$PATH"

# --- Dalamud dev libraries -----------------------------------------------------
# The Dalamud.NET.Sdk resolves references from ~/.xlcore/dalamud/Hooks/dev on Linux.
if [ ! -f "$DALAMUD_LIB_DIR/Dalamud.dll" ]; then
  echo "[install] Downloading Dalamud dev libraries to ${DALAMUD_LIB_DIR}"
  mkdir -p "$DALAMUD_LIB_DIR"
  tmp_zip="$(mktemp --suffix=.zip)"
  curl -sSL "$DALAMUD_ZIP_URL" -o "$tmp_zip"
  unzip -oq "$tmp_zip" -d "$DALAMUD_LIB_DIR"
  rm -f "$tmp_zip"
else
  echo "[install] Dalamud dev libraries already present in ${DALAMUD_LIB_DIR}"
fi

# --- Restore & build -----------------------------------------------------------
cd "$(dirname "$0")/.."
echo "[install] Restoring and building ActionStacksEX (Release)"
dotnet build ActionStacksEX.csproj -c Release

echo "[install] Done. Plugin package: bin/Release/ActionStacksEX/latest.zip"
