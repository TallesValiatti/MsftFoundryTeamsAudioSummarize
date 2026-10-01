# Teams Audio Summarizer with Microsoft Foundry

A Microsoft Teams bot that receives an **audio file** and replies with a **summary**. Text messages get a fixed reply.

```text
Teams ──► Azure Bot Service ──► MsftFoundryTeamsAudioSummarize.Web (App Service, .NET 10)
                                    │
                                    ├─► Foundry Speech to Text   (audio → transcript)
                                    └─► Foundry Agent Service    (transcript → summary, Foundry agent)
                                    │
Teams ◄──────── summary ◄───────────┘
```

## Repository

| Path | Description |
|---|---|
| `MsftFoundryTeamsAudioSummarize.Web/` | Bot backend (ASP.NET Core + Bot Framework SDK) |
| `teams/` | Teams manifest, icons and packaging instructions |
| `agent-instructions.md` | Instructions (with few-shot examples) for the Foundry agent |
| `deploy.sh` | Publishes and ZIP-deploys the backend to App Service |
| `Articles/raw.md` | Article outline |

## How it works

1. `BotController` (`/api/messages`) validates the Azure Bot Service token.
2. `AudioSummarizerBot` looks for an audio attachment. No audio → fixed message.
3. It replies "processing" right away and continues in the background (Azure Bot Service times out after ~15 s).
4. `AudioDownloader` downloads the file → `SpeechTranscriptionService` transcribes it → `SummarizerAgentService` calls the Foundry agent (single-turn, Responses API).
5. The summary is sent back with a proactive message.

Supported formats: `wav`, `mp3`, `ogg`, `opus`, `flac`, `wma`, `aac`, `amr`, `webm` (max 200 MB). For `mp4`/`m4a`, extract the audio first (e.g. `ffmpeg -i in.mp4 -vn out.wav`).

## Identities

Two app registrations, each with a single responsibility:

| App | Id | Used for |
|---|---|---|
| Bot | `<bot-app-id>` | Teams manifest, Azure Bot Service, `/api/messages` auth |
| Foundry | `<foundry-app-id>` | Speech to Text + Agent Service |

Roles for the Foundry app on the Foundry **resource** (assigned by a Foundry Owner). Speech is called on the resource endpoint, so `Cognitive Services Speech User` must be on the resource, not only on the project:

```bash
FOUNDRY_APP_ID="<foundry-app-id>"
RESOURCE_GROUP="<resource-group>"
FOUNDRY_RESOURCE="<foundry-resource>"
SCOPE=$(az cognitiveservices account show -g "$RESOURCE_GROUP" -n "$FOUNDRY_RESOURCE" --query id -o tsv)

az role assignment create --assignee "$FOUNDRY_APP_ID" --role "Cognitive Services Speech User" --scope "$SCOPE"
az role assignment create --assignee "$FOUNDRY_APP_ID" --role "Foundry User" --scope "$SCOPE"
```

## Configuration

Set in `appsettings.json` locally, or as App Service environment variables (added manually):

| Variable | Value |
|---|---|
| `MicrosoftAppType` | `SingleTenant` |
| `MicrosoftAppId` | `<bot-app-id>` |
| `MicrosoftAppPassword` | Bot app secret |
| `MicrosoftAppTenantId` | `<tenant-id>` |
| `Foundry__TenantId` | `<tenant-id>` |
| `Foundry__ClientId` | `<foundry-app-id>` |
| `Foundry__ClientSecret` | Foundry app secret |
| `Foundry__ProjectEndpoint` | `https://<foundry-resource>.services.ai.azure.com/api/projects/<foundry-project>` |
| `Foundry__AgentName` | `<agent-name>` |
| `Foundry__SpeechEndpoint` | `https://<foundry-resource>.cognitiveservices.azure.com/` |
| `Foundry__SpeechLocales__0` | Optional, e.g. `pt-BR`. Empty = automatic language detection |

`appsettings.json` only has placeholders (`<bot-app-id>`, `<foundry-app-id>`, `<tenant-id>`); the app fails at startup if one is not replaced. Never commit real ids or secrets — use environment variables or a git-ignored `appsettings.Development.json`.

## Setup

1. **Agent**: create an agent (e.g. model `gpt-6-luna`) in the Foundry portal and paste `agent-instructions.md`.
2. **Deploy**: `SUBSCRIPTION_ID=<subscription-id> RESOURCE_GROUP=<resource-group> WEB_APP_NAME=<app-service-name> ./deploy.sh` (uses your local `az login`).
3. **Azure Bot Service** (`<bot-service-name>`):
   - Messaging endpoint: `https://<app-service-hostname>/api/messages`
   - Channels → enable **Microsoft Teams**.
4. **Teams app**: `BOT_APP_ID=<bot-app-id> APP_SERVICE_HOSTNAME=<app-service-hostname> ./teams/package.sh` and upload the zip ([teams/README.md](teams/README.md)).

## Run locally

```bash
cd MsftFoundryTeamsAudioSummarize.Web
dotnet run
```

Expose it with a tunnel (e.g. `devtunnel host -p 5258 --allow-anonymous`) and point the bot messaging endpoint to `<tunnel>/api/messages`. Health check: `/health`.

## Limitation: channels and group chats

The bot can be added to personal chats, group chats and channels (public and private), but Teams only delivers **downloadable** files to bots in **personal chat**. In channels and group chats a file arrives as a SharePoint/OneDrive link, which would require Microsoft Graph permissions; the bot detects it and asks the user to send the audio in a personal chat.
