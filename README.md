# Mattermost MCP

[![CI](https://github.com/iamr8/Mattermost-MCP/actions/workflows/ci.yml/badge.svg)](https://github.com/iamr8/Mattermost-MCP/actions/workflows/ci.yml)
[![License: PolyForm Noncommercial](https://img.shields.io/badge/license-PolyForm%20Noncommercial-blue)](LICENSE)

Let your AI app read and write in your Mattermost chat - only in the channels you choose.

You ask your AI app things like "What did the team say about the release today?" or "Post a short summary in the design channel." This small program connects the app to Mattermost, so it can do it for you.

> [!NOTE]
> Free for personal use. Commercial use is not allowed. See [License](#license).

## What it can do

- **Read** - list your channels, read recent messages and threads, search messages, see who a user is.
- **Files** - see the files on a message and save one to your computer (up to 25 MB).
- **Write** (only if you turn it on) - post a message, reply in a thread, add an emoji reaction, edit your own message.

It keeps you safe:

- It sees **only the channels you allow**. Everything else stays hidden.
- It is **read-only by default**. It cannot write until you turn writing on.
- It never sends **@all, @channel or @here**, so it cannot notify a whole team.
- It writes **at most 20 times in 10 minutes**.
- It never shows **email addresses**.

## What you need

- A Mattermost account on a server that uses `https`.
- A **personal access token** from Mattermost. Open your profile, go to **Security**, then **Personal Access Tokens**, and create one. If you cannot see this option, ask your Mattermost admin to turn it on.
- An AI app that supports MCP, for example Claude Desktop, Cursor or VS Code.

## Install

1. Open the [Releases page](https://github.com/iamr8/Mattermost-MCP/releases/latest).
2. Download the file for your computer:

   | Computer | File |
   |---|---|
   | Mac with Apple chip (M1 or newer) | `MattermostMCP-…-osx-arm64.tar.gz` |
   | Mac with Intel chip | `MattermostMCP-…-osx-x64.tar.gz` |
   | Windows | `MattermostMCP-…-win-x64.zip` |
   | Linux | `MattermostMCP-…-linux-x64.tar.gz` |

3. Unpack it into a folder you keep, for example `Documents/MattermostMCP`.

You do not need to install anything else.

> [!TIP]
> **Mac:** macOS may block a program it does not know. Open **Terminal** and run this once (use your own folder):
>
> ```sh
> xattr -d com.apple.quarantine ~/Documents/MattermostMCP/MattermostMCP
> ```
>
> **Windows:** if a blue "Windows protected your PC" window shows, click **More info**, then **Run anyway**.

## Settings

You give the program four settings. Your AI app passes them on when it starts the program.

| Setting | What it means | Example |
|---|---|---|
| `MATTERMOST_URL` | The address of your Mattermost server. | `https://chat.example.com` |
| `MATTERMOST_TOKEN` | Your personal access token. Keep it secret. | |
| `MATTERMOST_ALLOWLIST` | The channels the AI app may use. See below. | `myteam/general,@alex` |
| `MATTERMOST_READ_ONLY` | `true` (default) or `false`. Set `false` to let the AI app write. | `true` |

### Choose the channels

`MATTERMOST_ALLOWLIST` is a list, split by commas. Each item can be:

| Item | Allows |
|---|---|
| `myteam` | every channel of the team `myteam` |
| `myteam/general` | only the channel `general` in `myteam` |
| `@alex` | your direct messages with the user `alex` |
| `*` | every channel you are in (use it alone) |

Use the names you see in the Mattermost web address. For example, `https://chat.example.com/myteam/channels/general` is `myteam/general`.

## Connect your AI app

### Claude Desktop

Open **Settings**, then **Developer**, then **Edit Config**. Add this to the file, with your own values, and restart the app:

```json
{
  "mcpServers": {
    "mattermost": {
      "command": "/Users/you/Documents/MattermostMCP/MattermostMCP",
      "env": {
        "MATTERMOST_URL": "https://chat.example.com",
        "MATTERMOST_TOKEN": "your-token",
        "MATTERMOST_ALLOWLIST": "myteam/general,@alex",
        "MATTERMOST_READ_ONLY": "true"
      }
    }
  }
}
```

On Windows, the `command` looks like `C:\\Users\\you\\Documents\\MattermostMCP\\MattermostMCP.exe`.

### Cursor

Open **Settings**, then **MCP**, then **Add new MCP server**. Use the same text as for Claude Desktop.

### VS Code

Run **MCP: Add Server** from the command palette, or create `.vscode/mcp.json`. Use the same values, but the top key is `servers` and each server needs `"type": "stdio"`:

```json
{
  "servers": {
    "mattermost": {
      "type": "stdio",
      "command": "/Users/you/Documents/MattermostMCP/MattermostMCP",
      "env": {
        "MATTERMOST_URL": "https://chat.example.com",
        "MATTERMOST_TOKEN": "your-token",
        "MATTERMOST_ALLOWLIST": "myteam/general",
        "MATTERMOST_READ_ONLY": "true"
      }
    }
  }
}
```

### Check that it works

Ask your AI app: **"Who am I in Mattermost?"** It answers with your user name, your teams and the channels it may use.

## Problems

| You see | Do this |
|---|---|
| `MATTERMOST_URL is not set` (or another setting) | Add the missing setting to your app's config, then restart the app. |
| `must use https` | Use the `https://` address of your server. |
| The app cannot find a channel | Add the channel to `MATTERMOST_ALLOWLIST`. |
| The app says it cannot write | Set `MATTERMOST_READ_ONLY` to `false`. |
| Nothing happens | Check that your computer can open your Mattermost server in a browser. |

## For developers

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
dotnet build Mattermost.slnx
dotnet test --solution Mattermost.slnx
```

A new release starts when a tag like `v1.0.0` is pushed.

## License

[PolyForm Noncommercial 1.0.0](LICENSE). You may use, change and share this program for personal and other noncommercial use. You may not use it for commercial purposes.
