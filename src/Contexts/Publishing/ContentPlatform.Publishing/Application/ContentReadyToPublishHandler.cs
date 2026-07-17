using System.Text.Json;
using System.Text.RegularExpressions;
using ContentPlatform.Abstractions;
using ContentPlatform.Editorial.Contracts;
using ContentPlatform.Publishing.Domain;
using ContentPlatform.SharedKernel;
using Microsoft.Extensions.Logging;

namespace ContentPlatform.Publishing.Application;

/// <summary>
/// Yayına-hazır içeriği hedeflere dağıtır. Hedefler role göre çözülür (Editorial/Test; AdminInbox HARİÇ).
/// Her hedef için bir Publication (idempotent) oluşturulur ve DistributionService ile yayınlanır.
/// </summary>
public sealed class ContentReadyToPublishHandler(
    IPublicationTargetResolver targetResolver,
    IPublicationRepository publications,
    DistributionService distribution,
    ISettingsProvider settings,
    ISchedulePlanner planner,
    IClock clock,
    ILogger<ContentReadyToPublishHandler> logger)
    : IIntegrationEventHandler<ContentReadyToPublishIntegrationEvent>
{
    public async Task HandleAsync(ContentReadyToPublishIntegrationEvent e, CancellationToken ct)
    {
        const Channel channel = Channel.Telegram; // MVP; diğer kanallar aynı desenle

        var targets = await targetResolver.ResolveAsync(e.CategoryId, e.TestMode, channel, ct);
        if (targets.Count == 0)
        {
            logger.LogInformation("Yayın hedefi yok (kategori={Cat}, test={Test}).", e.CategoryId, e.TestMode);
            return;
        }

        // Yayın zamanı: TEST içerik daima HEMEN (test kanalı). Aksi halde elle verilen zaman,
        // o da yoksa kategori kadans politikasından bir sonraki slot. null → hemen.
        DateTimeOffset? scheduledAt = null;
        if (!e.TestMode)
            scheduledAt = e.ScheduledAt ?? await planner.NextSlotAsync(e.CategoryId, ct);

        var (buttonUrl, buttonText) = await BuildDetailButtonAsync(e, ct);
        var payloadJson = JsonSerializer.Serialize(new PublicationPayload(e.Title, e.ShortX, e.Tags, e.MediaUrl, e.Link, buttonUrl, buttonText));

        foreach (var target in targets)
        {
            var existing = await publications.FindAsync(e.ContentItemId, channel, target.ExternalTargetId, ct);
            if (existing is { Status: PublicationStatus.Published }) continue; // idempotent

            var pub = existing;
            if (pub is null)
            {
                pub = new Publication(e.ContentItemId, channel, target.SocialAccountId, target.ExternalTargetId, payloadJson,
                    e.CategoryId, scheduledAt, clock);
                await publications.AddAsync(pub, ct);
            }

            // Gelecek bir zamana planlıysa şimdi gönderme; ScheduledDispatchJob zamanı gelince gönderir.
            if (pub.Status == PublicationStatus.Scheduled)
            {
                await publications.SaveChangesAsync(ct);
                continue;
            }

            await distribution.PublishOneAsync(pub, ct);
            await publications.SaveChangesAsync(ct);
        }

        if (scheduledAt is { } at)
            logger.LogInformation("İçerik {Id} {Time} için planlandı ({Count} hedef).", e.ContentItemId, at, targets.Count);
    }
    /// <summary>
    /// Gonderi altindaki "Haber ayrintisi" butonunu kurar. AdGate ise Mini App derin linki (Adsgram reklami),
    /// degilse dogrudan makale linki. Link yoksa (test icerik) buton yok.
    /// </summary>
    private async Task<(string? Url, string? Text)> BuildDetailButtonAsync(ContentReadyToPublishIntegrationEvent e, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(e.Link)) return (null, null);
        const string text = "Haber ayrıntısı için tıkla";

        var url = e.Link;
        if (e.AdGate)
        {
            var miniapp = await settings.GetAsync("telegram.miniapp_url", ct);
            if (!string.IsNullOrWhiteSpace(miniapp))
            {
                var i = e.Link.IndexOf("/blog/", StringComparison.Ordinal);
                var slug = i >= 0 ? e.Link[(i + 6)..] : "";
                slug = Regex.Replace(slug, "[^A-Za-z0-9_-]", "");
                var sep = miniapp.Contains('?') ? "&" : "?";
                url = $"{miniapp}{sep}startapp={slug}";
            }
        }

        // Telegram inline buton URL'i herkese açık geçerli bir http(s) adresi olmalı.
        // localhost/relative/geçersiz ise butonu ekleme (gönderi yine de gitsin, tüm yayın patlamasın).
        if (!IsPublicHttpUrl(url))
        {
            logger.LogWarning("Buton URL'i geçersiz/herkese açık değil, buton eklenmedi: {Url}", url);
            return (null, null);
        }
        return (url, text);
    }

    private static bool IsPublicHttpUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return false;
        if (string.Equals(u.Host, "localhost", StringComparison.OrdinalIgnoreCase)) return false;
        if (u.Host is "127.0.0.1" or "::1") return false;
        return true;
    }
}

