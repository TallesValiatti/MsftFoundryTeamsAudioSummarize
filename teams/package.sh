#!/usr/bin/env bash
set -euo pipefail

# Creates the Teams app package (manifest + icons at the zip root).
# The committed manifest only has placeholders; real values are injected here.
#
# Usage:
#   BOT_APP_ID=<bot-app-id> APP_SERVICE_HOSTNAME=<app-service-hostname> ./teams/package.sh

: "${BOT_APP_ID:?Set BOT_APP_ID (Azure Bot Service app id)}"
: "${APP_SERVICE_HOSTNAME:?Set APP_SERVICE_HOSTNAME (e.g. <app-service-name>.azurewebsites.net)}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_DIR="${SCRIPT_DIR}/audio-summarizer"
ZIP_PATH="${SCRIPT_DIR}/audio-summarizer.zip"
BUILD_DIR="$(mktemp -d)"
trap 'rm -rf "$BUILD_DIR"' EXIT

sed -e "s/<bot-app-id>/${BOT_APP_ID}/g" \
    -e "s/<app-service-hostname>/${APP_SERVICE_HOSTNAME}/g" \
    "${APP_DIR}/manifest.json" > "${BUILD_DIR}/manifest.json"

cp "${APP_DIR}/color.png" "${APP_DIR}/outline.png" "$BUILD_DIR/"

rm -f "$ZIP_PATH"
(cd "$BUILD_DIR" && zip -q "$ZIP_PATH" manifest.json color.png outline.png)

echo "Package created: $ZIP_PATH"
