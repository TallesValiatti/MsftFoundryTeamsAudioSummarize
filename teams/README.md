# Teams app package

`audio-summarizer/` contains the Teams manifest and icons. The committed manifest uses placeholders (`<bot-app-id>`, `<app-service-hostname>`) so real ids are never pushed to git.

## 1. Zip

```bash
BOT_APP_ID=<bot-app-id> APP_SERVICE_HOSTNAME=<app-service-hostname> ./teams/package.sh
```

It injects the values and creates `teams/audio-summarizer.zip` (git-ignored) with `manifest.json`, `color.png` and `outline.png` at the zip root.

Bump `version` in `manifest.json` before each re-upload.

## 2. Upload

1. Open https://admin.cloud.microsoft/#/agents/overview
2. **Agents** > **All agents** > **Upload custom agent**.
3. Select `teams/audio-summarizer.zip` and confirm.
4. Make the agent available to the users who need it.

Users can then add **Audio Summarizer** to a personal chat, group chat or team channel (public or private). Audio files are summarized in personal chat; see the main README for the channel/group chat limitation.
