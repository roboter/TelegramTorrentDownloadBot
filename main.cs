#:package Telegram.Bot@22.10.0.1
#:package System.Text.Encoding.CodePages@10.0.0

using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

class Program
{
    static async Task Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var botToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");

        if (string.IsNullOrWhiteSpace(botToken))
        {
            Console.WriteLine("Missing TELEGRAM_BOT_TOKEN env variable");
            return;
        }

        var bot = new TelegramBotClient(botToken);

        var allowedUsers = new HashSet<long>
        {
        /// ADD USER ID HERE
        };

        var incomingFolder = ExpandPath("~/Downloads");
        Directory.CreateDirectory(incomingFolder);

        using var cts = new CancellationTokenSource();

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = Array.Empty<UpdateType>()
        };

        bot.StartReceiving(
            updateHandler: (client, update, token) =>
                HandleUpdate(client, update, token, allowedUsers, incomingFolder),
            errorHandler: HandleError,
            receiverOptions: receiverOptions,
            cancellationToken: cts.Token
        );

        Console.WriteLine("Bot is running...");
        Console.ReadLine();

        cts.Cancel();
    }

    static async Task HandleUpdate(
        ITelegramBotClient bot,
        Update update,
        CancellationToken cancellationToken,
        HashSet<long> allowedUsers,
        string incomingFolder)
    {
        try
        {
            Console.WriteLine($"Received update: id={update.Id}, type={update.Type}");

            if (update.Message == null)
            {
                Console.WriteLine("Ignored update: no message.");
                return;
            }

            Console.WriteLine(
                $"Received message: id={update.Message.MessageId}, chat={update.Message.Chat.Id}, chatType={update.Message.Chat.Type}");

            if (update.Message.From == null)
            {
                Console.WriteLine("Ignored message: no sender.");
                return;
            }

            // Only private chats
            if (update.Message.Chat.Type != ChatType.Private)
            {
                Console.WriteLine($"Ignored message: not a private chat ({update.Message.Chat.Type}).");
                return;
            }

            var userId = update.Message.From.Id;
            Console.WriteLine($"Message sender: id={userId}, username={update.Message.From.Username ?? "<none>"}");

            // BLOCK everyone else
            if (!allowedUsers.Contains(userId))
            {
                Console.WriteLine($"Blocked user: {userId}");
                return;
            }

            var chatId = update.Message.Chat.Id;
            var document = update.Message.Document;
            var text = update.Message.Text;

            Console.WriteLine(
                $"Message payload: hasText={!string.IsNullOrWhiteSpace(text)}, hasDocument={document != null}");

            if (!string.IsNullOrWhiteSpace(text))
                Console.WriteLine($"Message text: {Shorten(text)}");

            if (document != null)
            {
                Console.WriteLine(
                    $"Document: fileName={document.FileName ?? "<none>"}, mimeType={document.MimeType ?? "<none>"}, fileId={Shorten(document.FileId)}");
            }

            // TORRENT FILE ATTACHMENT
            if (document != null)
            {
                Console.WriteLine("Parsing as Telegram document attachment.");

                var originalFileName = document.FileName ?? "";
                var isTorrentFile =
                    originalFileName.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(document.MimeType, "application/x-bittorrent", StringComparison.OrdinalIgnoreCase);

                if (!isTorrentFile)
                {
                    Console.WriteLine("Document rejected: not a .torrent file.");

                    await bot.SendMessage(
                        chatId,
                        "Send a .torrent file, magnet link, .torrent URL, or page URL",
                        cancellationToken: cancellationToken);

                    return;
                }

                var telegramFile = await bot.GetFile(document.FileId, cancellationToken);
                var fileName = GetSafeTorrentFileName(originalFileName);
                var path = Path.Combine(incomingFolder, fileName);
                path = GetUniquePath(path);

                Console.WriteLine($"Downloading Telegram document to: {path}");

                await using var stream = File.Create(path);
                await bot.DownloadFile(telegramFile, stream, cancellationToken);

                await bot.SendMessage(
                    chatId,
                    "✅ Torrent file saved",
                    cancellationToken: cancellationToken);

                return;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                Console.WriteLine("Ignored message: no supported text or document payload.");
                return;
            }

            // MAGNET LINK
            if (text.StartsWith("magnet:?"))
            {
                Console.WriteLine("Parsing as direct magnet link.");

                var fileName = $"{Guid.NewGuid()}.magnet";
                var path = Path.Combine(incomingFolder, fileName);

                await File.WriteAllTextAsync(path, text, cancellationToken);
                Console.WriteLine($"Saved magnet to: {path}");

                await bot.SendMessage(
                    chatId,
                    "✅ Magnet saved",
                    cancellationToken: cancellationToken);

                return;
            }

            // TORRENT FILE URL
            if (text.EndsWith(".torrent"))
            {
                Console.WriteLine("Parsing as direct .torrent URL.");

                using var http = CreateHttpClient();

                var path = await DownloadTorrentFile(
                    http,
                    text,
                    incomingFolder,
                    cancellationToken);

                Console.WriteLine($"Saved torrent URL download to: {path}");

                await bot.SendMessage(
                    chatId,
                    "✅ Torrent file saved",
                    cancellationToken: cancellationToken);

                return;
            }

            // WEB PAGE WITH MAGNET OR TORRENT LINK
            if (Uri.TryCreate(text, UriKind.Absolute, out var pageUrl) &&
                (pageUrl.Scheme == Uri.UriSchemeHttp || pageUrl.Scheme == Uri.UriSchemeHttps))
            {
                Console.WriteLine("Parsing as web page URL.");

                using var http = CreateHttpClient();
                var saved = await TrySaveTorrentFromPage(
                    http,
                    text,
                    incomingFolder,
                    cancellationToken);

                if (saved)
                {
                    await bot.SendMessage(
                        chatId,
                        "✅ Torrent from page saved",
                        cancellationToken: cancellationToken);

                    return;
                }

                Console.WriteLine("Web page URL parsed, but no magnet or torrent was saved.");
            }
            else
            {
                Console.WriteLine("Text is not a supported magnet, .torrent URL, or web page URL.");
            }

            await bot.SendMessage(
                chatId,
                "Send a .torrent file, magnet link, .torrent URL, or page URL",
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }

    static Task HandleError(
        ITelegramBotClient bot,
        Exception exception,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Telegram error: {exception.Message}");
        return Task.CompletedTask;
    }

    static string ExpandPath(string path)
    {
        if (path == "~")
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (path.StartsWith("~/"))
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                path[2..]);

        return path;
    }

    static async Task<bool> TrySaveTorrentFromPage(
        HttpClient http,
        string pageUrl,
        string incomingFolder,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Fetching page: {pageUrl}");

        var html = await GetString(http, pageUrl, cancellationToken);
        var pageTitle = GetPageTitle(html);

        Console.WriteLine($"Page title: {pageTitle}");
        LogPageScrapeDebug(html, new Uri(pageUrl));

        var magnet = FindMagnetLink(html);
        if (!string.IsNullOrWhiteSpace(magnet))
        {
            Console.WriteLine($"Found magnet: {Shorten(magnet)}");

            var fileName = GetSafeFileName(pageTitle, "magnet") + ".magnet";
            var path = GetUniquePath(Path.Combine(incomingFolder, fileName));

            await File.WriteAllTextAsync(path, magnet, cancellationToken);
            Console.WriteLine($"Saved magnet to: {path}");
            return true;
        }

        var torrentUrl = FindTorrentUrl(html, new Uri(pageUrl));
        if (torrentUrl == null)
        {
            Console.WriteLine("No magnet or torrent link found on page.");
            return false;
        }

        Console.WriteLine($"Found torrent URL: {torrentUrl}");

        var torrentPath = await DownloadTorrentFile(
            http,
            torrentUrl.ToString(),
            incomingFolder,
            cancellationToken,
            pageTitle);

        Console.WriteLine($"Saved torrent to: {torrentPath}");
        return true;
    }

    static HttpClient CreateHttpClient()
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (compatible; TorrentBot/1.0; +https://example.local)");

        return http;
    }

    static async Task<string> GetString(
        HttpClient http,
        string url,
        CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var charset = response.Content.Headers.ContentType?.CharSet?.Trim('"');
        var encoding = string.IsNullOrWhiteSpace(charset)
            ? Encoding.UTF8
            : Encoding.GetEncoding(charset);

        return encoding.GetString(data);
    }

    static string? FindMagnetLink(string html)
    {
        foreach (var content in GetDecodedVariants(html))
        {
            foreach (Match match in Regex.Matches(content, @"href\s*=\s*[""'](?<url>[^""']+)[""']", RegexOptions.IgnoreCase))
            {
                var url = DecodeLink(match.Groups["url"].Value);

                if (url.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                    return NormalizeMagnet(url);
            }

            var textMatch = Regex.Match(content, @"magnet:\?[^\s""'<>\\]+", RegexOptions.IgnoreCase);
            if (textMatch.Success)
                return NormalizeMagnet(textMatch.Value);
        }

        return null;
    }

    static Uri? FindTorrentUrl(string html, Uri pageUrl)
    {
        foreach (var content in GetDecodedVariants(html))
        {
            foreach (Match match in Regex.Matches(content, @"href\s*=\s*[""'](?<url>[^""']+)[""']", RegexOptions.IgnoreCase))
            {
                var href = DecodeLink(match.Groups["url"].Value);

                if (!LooksLikeTorrentLink(href))
                    continue;

                if (Uri.TryCreate(pageUrl, href, out var torrentUrl))
                    return torrentUrl;
            }
        }

        return null;
    }

    static IEnumerable<string> GetDecodedVariants(string html)
    {
        yield return html;

        var htmlDecoded = WebUtility.HtmlDecode(html);
        if (htmlDecoded != html)
            yield return htmlDecoded;

        var urlDecoded = WebUtility.UrlDecode(htmlDecoded);
        if (urlDecoded != htmlDecoded)
            yield return urlDecoded;

        var slashDecoded = DecodeJavaScriptEscapes(urlDecoded);
        if (slashDecoded != urlDecoded)
            yield return slashDecoded;
    }

    static string DecodeLink(string value)
    {
        return DecodeJavaScriptEscapes(WebUtility.UrlDecode(WebUtility.HtmlDecode(value)));
    }

    static string DecodeJavaScriptEscapes(string value)
    {
        return value
            .Replace("\\u0026", "&")
            .Replace("\\/", "/")
            .Replace("\\:", ":")
            .Replace("\\?", "?")
            .Replace("\\&", "&")
            .Replace("\\=", "=");
    }

    static string NormalizeMagnet(string magnet)
    {
        magnet = DecodeLink(magnet).Trim();
        var end = magnet.IndexOfAny(new[] { '"', '\'', '<', '>', ' ', '\r', '\n', '\t' });

        if (end >= 0)
            magnet = magnet[..end];

        return magnet;
    }

    static void LogPageScrapeDebug(string html, Uri pageUrl)
    {
        var hrefs = new List<string>();
        var torrentCandidates = new List<string>();
        var magnetCandidates = new List<string>();

        foreach (var content in GetDecodedVariants(html))
        {
            foreach (Match match in Regex.Matches(content, @"href\s*=\s*[""'](?<url>[^""']+)[""']", RegexOptions.IgnoreCase))
            {
                var href = DecodeLink(match.Groups["url"].Value);
                hrefs.Add(href);

                if (href.Contains("magnet", StringComparison.OrdinalIgnoreCase))
                    magnetCandidates.Add(href);

                if (LooksLikeTorrentLink(href))
                    torrentCandidates.Add(href);
            }

            foreach (Match match in Regex.Matches(content, @"magnet:\?[^\s""'<>\\]+", RegexOptions.IgnoreCase))
                magnetCandidates.Add(NormalizeMagnet(match.Value));
        }

        Console.WriteLine($"Page bytes/chars: {Encoding.UTF8.GetByteCount(html)} bytes, {html.Length} chars");
        Console.WriteLine($"Found hrefs: {hrefs.Count}");
        Console.WriteLine($"Found magnet candidates: {magnetCandidates.Count}");
        LogCandidates("magnet candidate", magnetCandidates);
        Console.WriteLine($"Found torrent candidates: {torrentCandidates.Count}");
        LogCandidates("torrent candidate", torrentCandidates);

        if (torrentCandidates.Count > 0)
        {
            foreach (var candidate in torrentCandidates.GetRange(0, Math.Min(5, torrentCandidates.Count)))
            {
                if (Uri.TryCreate(pageUrl, candidate, out var absolute))
                    Console.WriteLine($"absolute torrent candidate: {absolute}");
            }
        }
    }

    static void LogCandidates(string label, List<string> candidates)
    {
        var seen = new HashSet<string>();
        var printed = 0;

        foreach (var candidate in candidates)
        {
            if (!seen.Add(candidate))
                continue;

            Console.WriteLine($"{label}: {Shorten(candidate)}");
            printed++;

            if (printed >= 5)
                return;
        }
    }

    static string Shorten(string value)
    {
        const int maxLength = 180;
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }

    static bool LooksLikeTorrentLink(string href)
    {
        return href.Contains(".torrent", StringComparison.OrdinalIgnoreCase) ||
               href.Contains("download.php", StringComparison.OrdinalIgnoreCase) ||
               href.Contains("dl.php", StringComparison.OrdinalIgnoreCase);
    }

    static async Task<string> DownloadTorrentFile(
        HttpClient http,
        string url,
        string incomingFolder,
        CancellationToken cancellationToken,
        string? fallbackName = null)
    {
        using var response = await http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var fileName = GetTorrentFileName(url, response.Content.Headers.ContentDisposition, fallbackName);
        var path = GetUniquePath(Path.Combine(incomingFolder, fileName));

        await using var stream = File.Create(path);
        await response.Content.CopyToAsync(stream, cancellationToken);

        return path;
    }

    static string GetTorrentFileName(
        string url,
        ContentDispositionHeaderValue? contentDisposition,
        string? fallbackName)
    {
        var headerFileName = contentDisposition?.FileNameStar ?? contentDisposition?.FileName;
        var fileName = string.IsNullOrWhiteSpace(headerFileName)
            ? Path.GetFileName(new Uri(url).LocalPath)
            : headerFileName.Trim('"');

        if (string.IsNullOrWhiteSpace(fileName) || !fileName.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
            fileName = GetSafeFileName(fallbackName, "torrent") + ".torrent";

        return GetSafeTorrentFileName(fileName);
    }

    static string GetPageTitle(string html)
    {
        var match = Regex.Match(html, @"<title[^>]*>(?<title>.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var title = match.Success ? WebUtility.HtmlDecode(match.Groups["title"].Value) : "";

        title = Regex.Replace(title, @"\s+", " ").Trim();

        if (title.Contains("::"))
            title = title.Split("::")[0].Trim();

        return title;
    }

    static string GetSafeFileName(string? fileName, string fallback)
    {
        fileName = string.IsNullOrWhiteSpace(fileName) ? fallback : fileName;
        fileName = Path.GetFileName(fileName);

        foreach (var invalidChar in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(invalidChar, '_');

        return string.IsNullOrWhiteSpace(fileName) ? fallback : fileName;
    }

    static string GetSafeTorrentFileName(string originalFileName)
    {
        var fileName = GetSafeFileName(originalFileName, $"{Guid.NewGuid()}.torrent");

        if (string.IsNullOrWhiteSpace(fileName))
            return $"{Guid.NewGuid()}.torrent";

        if (!fileName.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
            fileName += ".torrent";

        return fileName;
    }

    static string GetUniquePath(string path)
    {
        if (!File.Exists(path))
            return path;

        var directory = Path.GetDirectoryName(path) ?? "";
        var fileName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var i = 1; ; i++)
        {
            var candidate = Path.Combine(directory, $"{fileName}-{i}{extension}");

            if (!File.Exists(candidate))
                return candidate;
        }
    }
}
