using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ContentPlatform.Platform.Application;
using Microsoft.Extensions.DependencyInjection;
using PlatformKind = ContentPlatform.Abstractions.Platform;

namespace ContentPlatform.Worker;

/// <summary>
/// Telegram long-polling dinleyicisi: botun bulundugu sohbetlerde "/getid" (veya "/id", "/chatid")
/// komutuna o sohbetin chat_id'sini yanit olarak gonderir. Grup/kanal ID'sini panele girmeyi kolaylastirir.
///
/// YALNIZ Worker'da calisir (tek instance). Api pollememeli: ayni token'da iki getUpdates → 409 conflict.
/// Gonderim akisini ETKILEMEZ (ayri istek). Token(lar) sunucuda sifreli saklanan SocialAccount'tan cozulur.
/// </summary>
public sealed class TelegramCommandPoller(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    ILogger<TelegramCommandPoller> logger) : BackgroundService
{
    private readonly Dictionary<string, long> _offsets = new(); // token -> bir sonraki offset

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("TelegramCommandPoller basladi — '/getid' komutu dinleniyor.");
        // Yeniden baslatinca eski (bekleyen) komutlara yanit vermemek icin ilk turda sadece 'drain' et.
        var firstPass = true;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var tokens = await GetBotTokensAsync(stoppingToken);
                if (tokens.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                    continue;
                }

                foreach (var token in tokens)
                    await PollOnceAsync(token, drainOnly: firstPass, stoppingToken);

                firstPass = false;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "TelegramCommandPoller dongu hatasi");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }

    private async Task<IReadOnlyList<string>> GetBotTokensAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISocialAccountRepository>();
        var svc = scope.ServiceProvider.GetRequiredService<SocialAccountService>();
        var accounts = await repo.ListByPlatformAsync(PlatformKind.Telegram, ct);

        var tokens = new List<string>();
        foreach (var acc in accounts)
        {
            try
            {
                var creds = svc.DecryptCredentials(acc);
                if (creds.TryGetValue("BotToken", out var t) && !string.IsNullOrWhiteSpace(t))
                    tokens.Add(t.Trim());
            }
            catch (Exception ex) { logger.LogWarning(ex, "Bot token cozulemedi: hesap={Acc}", acc.Id); }
        }
        return tokens.Distinct().ToList();
    }

    private async Task PollOnceAsync(string token, bool drainOnly, CancellationToken ct)
    {
        // Varsayilan HttpClient (100 sn timeout, direncsiz) — 25 sn long-poll icin uygun.
        var client = httpClientFactory.CreateClient();
        var baseUrl = $"https://api.telegram.org/bot{token}";
        _offsets.TryGetValue(token, out var offset);

        // allowed_updates = ["message","channel_post"]
        var url = $"{baseUrl}/getUpdates?timeout=25&allowed_updates=%5B%22message%22%2C%22channel_post%22%5D";
        if (offset > 0) url += $"&offset={offset}";

        TgUpdatesResponse? resp;
        try
        {
            resp = await client.GetFromJsonAsync<TgUpdatesResponse>(url, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "getUpdates hatasi (webhook acik olabilir ya da ag sorunu)");
            return;
        }

        if (resp is null || !resp.Ok || resp.Result is null || resp.Result.Count == 0)
            return;

        var maxId = offset - 1;
        foreach (var u in resp.Result)
        {
            if (u.UpdateId > maxId) maxId = u.UpdateId;
            if (drainOnly) continue;

            var msg = u.Message ?? u.ChannelPost;
            if (msg?.Chat is null) continue;
            if (IsGetIdCommand(msg.Text))
                await ReplyIdAsync(baseUrl, client, msg.Chat, ct);
        }
        _offsets[token] = maxId + 1;
    }

    private static bool IsGetIdCommand(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var first = text.Trim().Split(new[] { ' ', '\n', '\t' }, 2)[0];
        var at = first.IndexOf('@'); if (at >= 0) first = first[..at]; // "/getid@Bot" → "/getid"
        return first.Equals("/getid", StringComparison.OrdinalIgnoreCase)
            || first.Equals("/id", StringComparison.OrdinalIgnoreCase)
            || first.Equals("/chatid", StringComparison.OrdinalIgnoreCase);
    }

    private async Task ReplyIdAsync(string baseUrl, HttpClient client, TgChat chat, CancellationToken ct)
    {
        var title = chat.Title ?? chat.Username ?? chat.FirstName ?? "(sohbet)";
        var text =
            "<b>Sohbet bilgisi</b>\n" +
            $"ID: <code>{chat.Id}</code>\n" +
            $"Tur: {chat.Type}\n" +
            $"Baslik: {System.Net.WebUtility.HtmlEncode(title)}\n\n" +
            "Panelde <b>Dis ID</b> alanina bu ID'yi girebilirsin.";
        try
        {
            using var r = await client.PostAsJsonAsync($"{baseUrl}/sendMessage",
                new { chat_id = chat.Id, text, parse_mode = "HTML" }, ct);
            logger.LogInformation("/getid yaniti gonderildi: chat={Id} ({Type})", chat.Id, chat.Type);
        }
        catch (Exception ex) { logger.LogWarning(ex, "/getid yaniti gonderilemedi: {Id}", chat.Id); }
    }

    private sealed record TgUpdatesResponse(
        [property: JsonPropertyName("ok")] bool Ok,
        [property: JsonPropertyName("result")] List<TgUpdate>? Result);

    private sealed record TgUpdate(
        [property: JsonPropertyName("update_id")] long UpdateId,
        [property: JsonPropertyName("message")] TgMessage? Message,
        [property: JsonPropertyName("channel_post")] TgMessage? ChannelPost);

    private sealed record TgMessage(
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("chat")] TgChat? Chat);

    private sealed record TgChat(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("username")] string? Username,
        [property: JsonPropertyName("first_name")] string? FirstName);
}
