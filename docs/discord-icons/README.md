# Discord Rich Presence Icons (5-Minute Portal Setup)

Clonedivers sends faction-matched Discord Rich Presence asset keys when a HellForge session starts:

- `clone-helmet` — Clones, Republic Commandos, and Regular modes (`docs/discord-icons/clone-helmet.png`, 512×512 transparent RGBA PNG)
- `empire-helmet` — EmpireDivers mode (`docs/discord-icons/empire-helmet.png`, 512×512 transparent RGBA PNG)

## Setup Steps in Discord Developer Portal

1. Open <https://discord.com/developers/applications> and click **New Application**.
2. Name the application **`Clonedivers`** (this is the bold title players see under "Playing **Clonedivers**").
3. Copy the **Application ID** from **General Information**.
4. In the left sidebar, open **Rich Presence → Art Assets** and click **Add Image(s)**:
   - Upload `clone-helmet.png` and keep its asset key as **`clone-helmet`**.
   - Upload `empire-helmet.png` and keep its asset key as **`empire-helmet`**.
   - Click **Save Changes** (Discord CDN propagation takes ~1–3 minutes).
5. Wire the Application ID into Clonedivers:
   - **Permanent release pin:** set `public const string DefaultAppId = "<YOUR_APP_ID>";` in `Clonedivers/DiscordRpcClient.cs`.
   - **Local testing without rebuilding:** set environment variable `CLONEDIVERS_DISCORD_APP_ID=<YOUR_APP_ID>`.

## Regenerating the Icons

Both PNGs are rendered directly from the launcher's `ForgeTheme` vector helmets:

```powershell
dotnet run --project Clonedivers/Clonedivers.csproj -c Release -- --export-discord-icons docs\discord-icons
```
