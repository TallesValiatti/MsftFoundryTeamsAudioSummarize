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

## How it works

1. `BotController` (`/api/messages`) validates the Azure Bot Service token.
2. `AudioSummarizerBot` looks for an audio attachment. In shared chats, optional Graph retrieval reads the triggering message and resolves its file. No audio → guidance.
3. It replies "processing" right away and continues in the background (Azure Bot Service times out after ~15 s).
4. `AudioDownloader` downloads the file → `SpeechTranscriptionService` transcribes it → `SummarizerAgentService` calls the Foundry agent (single-turn, Responses API).
5. The summary is sent back with a proactive message.

Supported formats: `wav`, `mp3`, `ogg`, `opus`, `flac`, `wma`, `aac`, `amr`, `webm` (max 200 MB). For `mp4`/`m4a`, extract the audio first (e.g. `ffmpeg -i in.mp4 -vn out.wav`).

## Identities

Two app registrations, each with a single responsibility:

| App | Id | Used for |
|---|---|---|
| Bot | `<bot-app-id>` | Teams manifest, Azure Bot Service, `/api/messages` auth, optional Graph file retrieval |
| Foundry | `<foundry-app-id>` | Speech to Text + Agent Service |

The Foundry app needs **Foundry User** on the Foundry **resource** (not only on the project), assigned by a Foundry Owner:

```bash
FOUNDRY_APP_ID="<foundry-app-id>"
RESOURCE_GROUP="<resource-group>"
FOUNDRY_RESOURCE="<foundry-resource>"
SCOPE=$(az cognitiveservices account show -g "$RESOURCE_GROUP" -n "$FOUNDRY_RESOURCE" --query id -o tsv)

az role assignment create --assignee "$FOUNDRY_APP_ID" --role "Foundry User" --scope "$SCOPE"
```

- **Resource scope**: Speech is called on the resource endpoint (`*.cognitiveservices.azure.com`); a role assigned only on the project does not apply to it.
- **Foundry User** (`Microsoft.CognitiveServices/*`) covers both the Agent Service and the Speech `transcriptions:transcribe` API. `Cognitive Services Speech User` alone returns `401 PermissionDenied` for this API. For a Speech-only alternative, use `Cognitive Services Speech Contributor`.
- RBAC changes can take a few minutes to apply. No restart or redeploy is needed.

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

## Troubleshooting

Failures are logged with the service status and error body (App Service → Log stream).

| Log | Cause | Fix |
|---|---|---|
| `Missing required configuration key '...'` | Setting missing or still a `<placeholder>` | Set the environment variable |
| `Name or service not known (<host>:443)` | Wrong endpoint host | `Foundry__SpeechEndpoint` must be `https://<foundry-resource>.cognitiveservices.azure.com/` (no `.ai`); `Foundry__ProjectEndpoint` uses `services.ai.azure.com` |
| Speech `401 PermissionDenied` — *Principal does not have access to API/Operation* | Missing role or wrong scope | Assign **Foundry User** on the Foundry resource (see [Identities](#identities)) |
| Agent `401`/`403` | Foundry app has no access to the project | Same as above |
| Agent `400 unsupported_parameter: 'reasoning.effort'` | The agent has a reasoning effort set, but its model does not support it | In the Foundry portal, clear **Reasoning effort** in the agent's model settings and save |
| Speech `401` right after a role change | Role still propagating | Wait a few minutes and retry |
| Bot never answers | Messaging endpoint or Teams channel not configured | See [Setup](#setup), step 3 |

## Audio in other chats and channels

Personal chat uploads work without Graph. For group chats and channels, enable Microsoft Graph retrieval. The bot reads the **triggering message**, resolves its audio reference through OneDrive/SharePoint, and downloads it before running the existing transcription and summary pipeline.

### Setup

1. In Entra ID, open the **Bot app registration** (`MicrosoftAppId`, not the Foundry app).
2. Under **API permissions**, add Microsoft Graph **Application** permissions:
   - `Chat.Read.All` to retrieve the triggering chat message.
   - `ChannelMessage.Read.All` if channel support is needed.
   - `Files.ReadWrite.All` for the Graph `/shares/{encodedUrl}/driveItem` endpoint. Microsoft documents this as the least privileged application permission for that endpoint, even though this implementation only makes GET requests.
3. Have a tenant administrator **grant admin consent**. These are tenant-wide permissions: chat/channel message access and read/write file access. Adding the bot to a chat does not grant these permissions.
4. Set `Graph__Enabled=true` in App Service environment variables and deploy the updated backend. Graph uses the existing `MicrosoftAppTenantId`, `MicrosoftAppId`, and `MicrosoftAppPassword` settings.
5. Attach an audio file and **@mention Audio Summarizer in the same message** in the other chat or channel. Mentioning the bot in a separate message does not select an earlier file. Only the first supported audio attachment is processed.

No manifest change is required for the existing `groupChat` and `team` scopes. This implementation targets the Microsoft public cloud and files accessible in the bot's tenant; cross-tenant/shared-channel file access is not guaranteed.

When Graph is disabled, shared chats get setup guidance. Permission errors get a specific reply. Graph lookup runs in the background to keep the bot webhook responsive. Personal audio uploads still use their original download path.

References: [Get a chat or channel message](https://learn.microsoft.com/en-us/graph/api/chatmessage-get?view=graph-rest-1.0), [Resolve shared files and required permissions](https://learn.microsoft.com/en-us/graph/api/shares-get?view=graph-rest-1.0).

### Local regression checks

```bash
dotnet run --project tests/GraphAudioChecks
```

These checks use simulated Graph responses and require no credentials. Validate actual chat uploads and channel replies in your tenant after granting consent and deploying.
