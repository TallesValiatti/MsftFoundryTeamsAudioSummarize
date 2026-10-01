#!/usr/bin/env bash
set -Eeuo pipefail

# ============================================================
# Deploys MsftFoundryTeamsAudioSummarize.Web to the existing
# Azure App Service using the local Azure CLI user (ZIP deploy).
#
# App settings (MicrosoftApp*, Foundry__*) are NOT set here.
# Configure them manually in the App Service.
#
# Usage:
#   chmod +x deploy.sh
#   SUBSCRIPTION_ID=<subscription-id> \
#   RESOURCE_GROUP=<resource-group> \
#   WEB_APP_NAME=<app-service-name> \
#   ./deploy.sh
# ============================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Values come from environment variables (see Usage); nothing environment-specific is committed.
SUBSCRIPTION_ID="${SUBSCRIPTION_ID:-<subscription-id>}"
RESOURCE_GROUP="${RESOURCE_GROUP:-<resource-group>}"
WEB_APP_NAME="${WEB_APP_NAME:-<app-service-name>}"

PROJECT_PATH="${SCRIPT_DIR}/MsftFoundryTeamsAudioSummarize.Web/MsftFoundryTeamsAudioSummarize.Web.csproj"
DEPLOY_DIR="${SCRIPT_DIR}/.deploy"
PUBLISH_DIR="${DEPLOY_DIR}/publish"
ZIP_PATH="${DEPLOY_DIR}/app.zip"

log() { printf '\n\033[1;34m==> %s\033[0m\n' "$1"; }
fail() { printf '\n\033[1;31mERROR: %s\033[0m\n' "$1" >&2; exit 1; }

for cmd in az dotnet zip; do
    command -v "$cmd" >/dev/null 2>&1 || fail "Command '$cmd' was not found."
done

require_value() {
    [[ -n "$2" && ! "$2" =~ ^\<.*\>$ ]] || fail "Set the $1 environment variable (see Usage in deploy.sh)."
}

require_value SUBSCRIPTION_ID "$SUBSCRIPTION_ID"
require_value RESOURCE_GROUP "$RESOURCE_GROUP"
require_value WEB_APP_NAME "$WEB_APP_NAME"

[[ -f "$PROJECT_PATH" ]] || fail "Project file not found: $PROJECT_PATH"

# ============================================================
# Azure authentication (local user)
# ============================================================
log "Authenticating with Azure CLI"

az account show >/dev/null 2>&1 || az login
az account set --subscription "$SUBSCRIPTION_ID"

az webapp show -g "$RESOURCE_GROUP" -n "$WEB_APP_NAME" --output none ||
    fail "App Service '$WEB_APP_NAME' was not found in '$RESOURCE_GROUP'."

# ============================================================
# Build and package
# ============================================================
log "Publishing the .NET application"

rm -rf "$DEPLOY_DIR"
mkdir -p "$PUBLISH_DIR"

dotnet publish "$PROJECT_PATH" --configuration Release --output "$PUBLISH_DIR"

log "Creating deployment package"
(cd "$PUBLISH_DIR" && zip -qr "$ZIP_PATH" .)

# ============================================================
# Deploy
# ============================================================
log "Deploying to Azure App Service"

az webapp deploy \
    -g "$RESOURCE_GROUP" \
    -n "$WEB_APP_NAME" \
    --src-path "$ZIP_PATH" \
    --type zip \
    --clean true \
    --restart true \
    --output none

HOST_NAME="$(az webapp show -g "$RESOURCE_GROUP" -n "$WEB_APP_NAME" --query defaultHostName -o tsv)"

log "Deployment completed"
echo "Health check:       https://${HOST_NAME}/health"
echo "Messaging endpoint: https://${HOST_NAME}/api/messages"
