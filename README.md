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

Before running, create a `.env` file in the project root and configure:

- `TELEGRAM_BOT_TOKEN` - Your Telegram bot token from BotFather
- `ALLOWED_USERS` - Comma-separated list of Telegram user IDs allowed to use the bot (get these from @userinfobot)
- `INCOMING_FOLDER` - (Optional) Folder where torrent files will be saved, defaults to `~/Downloads`

Example `.env` file:
```
TELEGRAM_BOT_TOKEN="123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11"
ALLOWED_USERS="123456789,987654321,111222333"
INCOMING_FOLDER="/opt/torrentbot/incoming"
```

Make sure the incoming folder is writable by the user running the bot:

```bash
mkdir -p /opt/torrentbot/incoming
chmod 755 /opt/torrentbot/incoming
```

## Check

```bash
dotnet run main.cs
```

## Run

The bot will automatically load settings from the `.env` file. Make sure you have
created the `.env` file as described above, then start the app:

```bash
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

Run the published app with (make sure `.env` file is in the same directory as the executable):

```bash
./publish/main
```