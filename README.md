# TorrentBot

A small Telegram bot that accepts `.torrent` files, magnet links, direct
`.torrent` URLs, or forum/topic pages containing a magnet or torrent link from
approved Telegram users and saves them into an incoming folder.

## Requirements

- .NET SDK 10.0 or newer
- A Telegram bot token from BotFather

## First-time setup

This repository is a .NET 10 file-based app. It does not need a solution or
`.csproj` file. The Telegram bot dependency is declared at the top of `main.cs`
with `#:package Telegram.Bot@22.10.0.1`.

Before running, edit `main.cs` and update:

- `allowedUsers` with your Telegram user IDs
- `incomingFolder` if you want to save files somewhere other than
  `/opt/torrentbot/incoming`

Make sure the incoming folder is writable by the user running the bot:

```bash
sudo mkdir -p /opt/torrentbot/incoming
sudo chown "$USER" /opt/torrentbot/incoming
```

## Check

```bash
dotnet run main.cs
```

## Run

Set your bot token and start the app:

```bash
export TELEGRAM_BOT_TOKEN="your_bot_token_here"
dotnet run main.cs
```

The bot prints `Bot is running...` when it has started. Send it a `.torrent`
file, magnet link, URL ending in `.torrent`, or a page URL that contains a
magnet or torrent download link from an allowed Telegram account.

## Publish a standalone build

To create a release build in `publish/`:

```bash
dotnet publish main.cs -c Release -o publish -p:PublishAot=false
```

Run the published app with:

```bash
export TELEGRAM_BOT_TOKEN="your_bot_token_here"
./publish/main
```
