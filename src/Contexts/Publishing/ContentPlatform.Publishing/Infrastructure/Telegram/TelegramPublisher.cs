using System.Net.Http.Json;
using ContentPlatform.Abstractions;
using ContentPlatform.SharedKernel;
using Microsoft.Extensions.Logging;

namespace ContentPlatform.Publishing.Infrastructure.Telegram;

/// <summary>
/// Telegram yayın adaptörü — ham Bot API. Görsel varsa:
///  - bytes (Media) → multipart sendPhoto (public URL gerekmez)
///  - yalnız MediaUrl → URL ile sendPhoto
///  - görsel yok → sendMessage
/// Kimlik (BotToken) ÇAĞRIDA gelir; hedef = chat/kanal id (TargetRef).
/// </summary>
internal sealed class TelegramPublisher(
    IHttpClientFactory httpClientFactory,
    ILogger<TelegramPublisher> logger) : IChannelPublisher
{
    public const string HttpClientName = "telegram";
    public Channel Channel => Channel.Telegram;

    public async Task<PublishResult> PublishAsync(PublishRequest request, AccountCredentials credentials, CancellationToken ct)
    {
        if (!credentials.Values.TryGetValue("BotToken", out var token) || string.IsNullOrWhiteSpace(token))
            return new PublishResult(false, null, Error.Validation("Telegram BotToken eksik."));

        var client = httpClientFactory.CreateClient(HttpClientName);
        var baseUrl = $"https://api.telegram.org/bot{token}";
        var caption = BuildText(request);

        try
        {
            HttpResponseMessage response;

            if (request.Media is { } media)
            {
                using var form = new MultipartFormDataContent
                {
                    { new StringContent(request.TargetRef), "chat_id" },
                    { new StringContent(caption), "caption" },
                    { new StringContent("HTML"), "parse_mode" }
                };
                var photo = new ByteArrayContent(media.Bytes);
                photo.Headers.TryAddWithoutValidation("Content-Type", media.ContentType);
                form.Add(photo, "photo", media.FileName);
                response = await client.PostAsync($"{baseUrl}/sendPhoto", form, ct);
            }
            else if (!string.IsNullOrWhiteSpace(request.MediaUrl))
            {
                response = await client.PostAsJsonAsync($"{baseUrl}/sendPhoto",
                    new { chat_id = request.TargetRef, photo = request.MediaUrl, caption, parse_mode = "HTML" }, ct);
            }
            else
            {
                response = await client.PostAsJsonAsync($"{baseUrl}/sendMessage",
                    new { chat_id = request.TargetRef, text = caption, parse_mode = "HTML" }, ct);
            }

            using (response)
            {
                var payload = await response.Content.ReadFromJsonAsync<TelegramResponse>(cancellationToken: ct);
                if (payload is { Ok: true })
                    return new PublishResult(true, payload.Result?.MessageId.ToString(), null);

                logger.LogWarning("Telegram yayın hatası: {Code} {Desc}", payload?.ErrorCode, payload?.Description);
                return new PublishResult(false, null, Error.Unexpected($"Telegram: {payload?.Description ?? response.StatusCode.ToString()}"));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Telegram istek hatası");
            return new PublishResult(false, null, Error.Unexpected($"Telegram istek hatası: {ex.Message}"));
        }
    }

    private static string BuildText(PublishRequest r)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(r.Title)) parts.Add($"<b>{r.Title}</b>");
        if (!string.IsNullOrWhiteSpace(r.Text)) parts.Add(r.Text);
        if (r.Hashtags.Count > 0) parts.Add(string.Join(' ', r.Hashtags));
        if (!string.IsNullOrWhiteSpace(r.Link)) parts.Add(r.Link);
        return string.Join("\n\n", parts);
    }

    private sealed record TelegramResponse(
        bool Ok,
        TelegramMessage? Result,
        [property: System.Text.Json.Serialization.JsonPropertyName("error_code")] int? ErrorCode,
        string? Description);

    private sealed record TelegramMessage(
        [property: System.Text.Json.Serialization.JsonPropertyName("message_id")] long MessageId);
}
