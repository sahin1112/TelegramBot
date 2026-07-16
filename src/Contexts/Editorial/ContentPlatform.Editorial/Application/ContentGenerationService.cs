using System.Text.Json;
using ContentPlatform.Abstractions;
using ContentPlatform.Editorial.Contracts;
using ContentPlatform.Editorial.Domain;
using ContentPlatform.SharedKernel;
using Microsoft.Extensions.Logging;

namespace ContentPlatform.Editorial.Application;

/// <summary>
/// Onaylanan içerik için AI metnini tek çağrıda üretir, sonra görseli oluşturur:
///  - ImageSource.Manual  → "Ben yükleyeceğim": AwaitingManualUpload'ta bekler (sıraya girmez).
///  - ImageSource.Ai      → AI görsel; başarısızsa SkiaCard'a düşer (görselsiz makale imkânsız).
///  - ImageSource.SkiaCard→ başlık kartı.
/// Görsel hazır olunca yayına-hazır olayı yayınlanır.
/// </summary>
public sealed class ContentGenerationService(
    IContentRepository repository,
    ITextGenerationProvider textProvider,
    IImageGenerationProvider imageProvider,
    ICardRenderer cardRenderer,
    IMediaStore mediaStore,
    IIntegrationEventPublisher bus,
    IClock clock,
    ILogger<ContentGenerationService> logger)
{
    private const int CardW = 1200, CardH = 675;

    private const string SystemPrompt =
        "Sen bir icerik editorusun. Kaynak metinden ozgun, telifsiz, atifli icerik uret; uydurma bilgi ekleme. " +
        "Paragraflar ARASINDA gercek cift satir sonu birak (\\n\\n); metni tek paragrafta BIRLESTIRME. " +
        "bodyHtml paragraflari <p>...</p> ile sar. " +
        "Cikti YALNIZCA su JSON: {\"title\":\"...\",\"shortX\":\"<=280 karakter\",\"bodyHtml\":\"ana makale HTML\"," +
        "\"instagramCaption\":\"<=2200\",\"tags\":[\"...\"],\"primaryKeyword\":\"...\",\"imageAltText\":\"...\"}";

    public async Task<int> GenerateForApprovedAsync(CancellationToken ct)
    {
        var items = await repository.GetForGenerationAsync(20, ct);
        var produced = 0;

        foreach (var item in items)
        {
            try
            {
                Fields f;
                if (item.UseAi)
                {
                    var input = $"Baslik: {item.RawTitle}\n\nMetin: {item.RawInput}";
                    var result = await textProvider.GenerateAsync(new TextGenerationRequest(SystemPrompt, input, "tr", "v1"), ct);
                    f = ParseFields(result.RawJson, item.RawTitle ?? "Baslik");
                    item.AddRevision(new ContentRevision(
                        item.Id, item.Revisions.Count + 1, f.Title, f.ShortX, f.BodyHtml, f.InstagramCaption,
                        f.Tags.ToList(), f.PrimaryKeyword, f.ImageAltText, createdBy: "ai", clock));
                }
                else
                {
                    // AI'sız (ManualNoAi): revizyon ekleme anında oluşturuldu; ondan üret.
                    var rev = item.Revisions.FirstOrDefault(r => r.IsCurrent);
                    if (rev is null) { logger.LogWarning("Manuel(AI'siz) icerik revizyonsuz: {Id}", item.Id); continue; }
                    f = new Fields(rev.Title, rev.ShortX, rev.BodyHtml, rev.InstagramCaption, rev.Tags, rev.PrimaryKeyword, rev.ImageAltText);
                }

                if (item.ImageSource == ImageSource.Manual)
                {
                    item.MarkAwaitingManualImage(clock);   // sen yukleyene kadar sirada beklemez
                    await repository.SaveChangesAsync(ct);
                    logger.LogInformation("Gorsel bekleniyor (elle): {Id}", item.Id);
                    continue;
                }

                var (url, kind, w, h) = await BuildImageAsync(item.ImageSource, f.Title, ct);
                item.AddMedia(kind, url, w, h, titleBurned: kind == MediaKind.SkiaCard, clock);
                item.MarkMediaReady(clock);
                await repository.SaveChangesAsync(ct);

                await PublishReadyAsync(item, f, url, ct);
                produced++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "AI uretimi basarisiz: {Id}", item.Id);
            }
        }

        if (produced > 0) logger.LogInformation("{Count} icerik uretildi ve yayina hazir.", produced);
        return produced;
    }

    /// <summary>"Ben yükleyeceğim" akışı: görsel elle yüklendi → hazır işaretle → yayına-hazır olayı.</summary>
    public async Task<Result> AttachManualImageAsync(Guid contentItemId, byte[] bytes, string contentType, CancellationToken ct)
    {
        var item = await repository.GetAsync(contentItemId, ct);
        if (item is null) return Result.Failure(Error.NotFound("İçerik"));
        if (item.MediaStatus != MediaStatus.AwaitingManualUpload)
            return Result.Failure(Error.Conflict("İçerik elle görsel beklemiyor."));

        var url = await mediaStore.SaveAsync(bytes, contentType, ct);
        item.AddMedia(MediaKind.Manual, url, 0, 0, titleBurned: false, clock);
        item.MarkMediaReady(clock);
        await repository.SaveChangesAsync(ct);

        var rev = item.Revisions.FirstOrDefault(r => r.IsCurrent);
        if (rev is not null)
            await PublishReadyAsync(item,
                new Fields(rev.Title, rev.ShortX, rev.BodyHtml, rev.InstagramCaption, rev.Tags, rev.PrimaryKeyword, rev.ImageAltText),
                url, ct);
        return Result.Success();
    }

    private async Task<(string Url, MediaKind Kind, int W, int H)> BuildImageAsync(ImageSource source, string title, CancellationToken ct)
    {
        if (source == ImageSource.Ai)
        {
            try
            {
                var img = await imageProvider.GenerateAsync(
                    new ImageGenerationRequest($"{title} — editoryal, metin icermeyen gorsel", 1024, 1024, "low"), ct);
                if (img.Bytes.Length > 0)
                {
                    var aiUrl = await mediaStore.SaveAsync(img.Bytes, img.ContentType, ct);
                    return (aiUrl, MediaKind.AiImage, 1024, 1024);
                }
            }
            catch (Exception ex) { logger.LogWarning(ex, "AI gorsel basarisiz, SkiaCard'a dusuluyor."); }
        }

        var card = cardRenderer.RenderTitleCard(title, null, CardW, CardH);
        var url = await mediaStore.SaveAsync(card, "image/png", ct);
        return (url, MediaKind.SkiaCard, CardW, CardH);
    }

    private Task PublishReadyAsync(ContentItem item, Fields f, string? mediaUrl, CancellationToken ct) =>
        bus.PublishAsync(new ContentReadyToPublishIntegrationEvent(
            Guid.NewGuid(), clock.UtcNow, item.Id, item.CategoryId, item.TestMode,
            f.Title, f.ShortX, f.BodyHtml, f.InstagramCaption, f.Tags.ToList(), mediaUrl, Link: null,
            ScheduledAt: item.ScheduledAt), ct);

    private static Fields ParseFields(string json, string fallbackTitle)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            string S(string k, string d = "") => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : d;
            var tags = r.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Array
                ? t.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList()
                : new List<string>();
            return new Fields(
                S("title", fallbackTitle), S("shortX"), S("bodyHtml"),
                S("instagramCaption") is { Length: > 0 } ig ? ig : null,
                tags, S("primaryKeyword") is { Length: > 0 } pk ? pk : null,
                S("imageAltText") is { Length: > 0 } alt ? alt : null);
        }
        catch { return new Fields(fallbackTitle, "", "", null, new List<string>(), null, null); }
    }

    private sealed record Fields(
        string Title, string ShortX, string BodyHtml, string? InstagramCaption,
        IReadOnlyList<string> Tags, string? PrimaryKeyword, string? ImageAltText);
}
