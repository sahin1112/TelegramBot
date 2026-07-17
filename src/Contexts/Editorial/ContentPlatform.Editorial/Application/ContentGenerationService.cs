using System.Text.Json;
using ContentPlatform.Abstractions;
using ContentPlatform.Editorial.Contracts;
using ContentPlatform.Editorial.Domain;
using ContentPlatform.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    IQualityGate qualityGate,
    IContentAudit audit,
    IKillSwitch killSwitch,
    IClock clock,
    IOptions<SiteOptions> siteOptions,
    ILogger<ContentGenerationService> logger)
{
    private const int CardW = 1200, CardH = 675;

    private const string SystemPrompt =
        "Sen SEO odakli bir HABER/icerik editorusun. Kaynak metni Turkce, ozgun, telifsiz ve ARAMA MOTORU DOSTU sekilde AKTAR; kaynakta olmayan isim/veri/bilgi UYDURMA. " +
        "DIL (COK ONEMLI): Kaynak hangi dilde olursa olsun (Ingilizce dahil) TUM CIKTIYI TURKCE uret. " +
        "ATIF / SADAKAT (COK ONEMLI, HER SENARYO): Kaynaktaki KIM-NE-NEREDE bilgisini KORU. Bir gorus/iddia kaynakta bir kisiye veya kuruma atfedilmisse (ornek: 'X, Y kurumu CEO'su'), icerikte de MUTLAKA o kisi/kuruma atfet; 'uzmanlar/yetkililer' gibi BELIRSIZ ifadeye cevirme ve sitenin kendi gorusuymus gibi SUNMA. Kaynakta acik bir kisi/kurum atfi YOKSA, atif UYDURMA (hayali 'uzman/yetkili' yazma); olayi tarafsiz haber diliyle, YALNIZCA kaynaktaki bilgiyle sinirli aktar. Sen yorum yapmiyorsun, kaynagi aktariyorsun. Bu kurallar her tur icerik icin gecerlidir: haber, duyuru, analiz, gorus, rehber. " +
        "BASLIK: Kaynagin basligini AYNEN KULLANMA/birebir cevirme; ama kaynaktaki ASIL KONUYU yansit (gerekiyorsa kisi/kurum adi gecebilir). Turkce, ozgun, SOMUT, ilgi cekici, SEO uyumlu; ana anahtar kelimeyi icersin; ~50-65 karakter. Genel/klise baslik yazma. " +
        "shortX (X/Twitter): <=280 karakter, TEK BASINA anlasilir NET bir OZET olsun — kim + ne dedi/ne oldu acik (kisi/kurum + iddia). Belirsiz teaser DEGIL; tweet tek basina okununca olay anlasilmali. " +
        "instagramCaption: <=2200 karakter, TEK BASINA anlasilir akici bir ozet; kim/ne/neden acik; kaynaktaki atfi koru. " +
        "GOVDE (bodyHtml): TURKCE, SEO uzunlugunda — EN AZ 600, mumkunse 700-1000 kelime. Kaynagin GERCEK konusuna odaklan; genel dolgu/klise ile sisirme. " +
        "Yapilandir: en az 2-3 adet <h2> (gerekirse <h3>) alt baslik; her paragrafi <p>...</p> ile sar; paragraflar ARASINDA gercek cift satir sonu (\\n\\n) birak, tek paragrafta BIRLESTIRME. " +
        "Ana anahtar kelimeyi (primaryKeyword) baslikta, ilk paragrafta ve alt basliklarda DOGAL kullan (asiri tekrar yok). Giris + alt baslikli govde + sonuc yapisi kur. " +
        "tags: 3-8 adet Turkce SEO etiketi. primaryKeyword: icerigin ana Turkce anahtar kelimesi. imageAltText: Turkce, anahtar kelimeli gorsel alt metni. " +
        "Cikti YALNIZCA su JSON: {\"title\":\"...\",\"shortX\":\"<=280 karakter\",\"bodyHtml\":\"uzun SEO makale HTML\"," +
        "\"instagramCaption\":\"<=2200\",\"tags\":[\"...\"],\"primaryKeyword\":\"...\",\"imageAltText\":\"...\"}";

    public async Task<int> GenerateForApprovedAsync(CancellationToken ct)
    {
        // Acil durdurma: AI (veya global) durdurulmuşsa bu tur hiç üretim yapma (içerik Approved kalır, sonra üretilir).
        if (await killSwitch.IsAiStoppedAsync(null, ct))
        {
            logger.LogWarning("AI üretimi durduruldu (kill-switch).");
            return 0;
        }

        var items = await repository.GetForGenerationAsync(20, ct);
        var produced = 0;

        foreach (var item in items)
        {
            // Kategori bazlı AI durdurma: o kategoriyi atla (içerik sırada bekler).
            if (await killSwitch.IsAiStoppedAsync(item.CategoryId, ct)) continue;
            try
            {
                Fields f;
                var current = item.Revisions.FirstOrDefault(r => r.IsCurrent);
                if (item.UseAi && current is null)
                {
                    // AI metni üret — YALNIZ henüz revizyon yoksa. Elle üretilen/düzenlenen içerik korunur (ezilmez).
                    var input = $"Baslik: {item.RawTitle}\n\nMetin: {item.RawInput}";
                    f = await RunTextAsync(input, item.RawTitle, ct);
                    item.AddRevision(new ContentRevision(
                        item.Id, item.Revisions.Count + 1, f.Title, f.ShortX, f.BodyHtml, f.InstagramCaption,
                        f.Tags.ToList(), f.PrimaryKeyword, f.ImageAltText, createdBy: "ai", clock));
                    audit.Log(item.Id, AuditEvent.Generated, ActorType.System, "ai");
                }
                else
                {
                    // Revizyon zaten var (elle AI üretimi / düzenleme / AI'sız manuel) → onu kullan, yeniden üretme.
                    if (current is null) { logger.LogWarning("Revizyonsuz içerik atlandı: {Id}", item.Id); continue; }
                    f = new Fields(current.Title, current.ShortX, current.BodyHtml, current.InstagramCaption, current.Tags, current.PrimaryKeyword, current.ImageAltText);
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

                await MaybePublishAsync(item, f, url, ct);
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
            await MaybePublishAsync(item,
                new Fields(rev.Title, rev.ShortX, rev.BodyHtml, rev.InstagramCaption, rev.Tags, rev.PrimaryKeyword, rev.ImageAltText),
                url, ct);
        return Result.Success();
    }

    /// <summary>
    /// Detay/panel ekranından görseli HEMEN üretir (AI veya SkiaCard) — YAYINLAMAZ, yalnız önizleme/hazırlık.
    /// Manual seçilirse görsel üretmez; içeriği elle yüklemeye hazır hale getirir. Üretilen görselin URL'sini döner.
    /// </summary>
    public async Task<Result<string?>> GeneratePreviewImageAsync(Guid contentItemId, ImageSource source, CancellationToken ct)
    {
        var item = await repository.GetAsync(contentItemId, ct);
        if (item is null) return Result.Failure<string?>(Error.NotFound("İçerik"));

        item.SetImageSource(source, clock);
        if (source == ImageSource.Manual)
        {
            item.MarkAwaitingManualImage(clock);
            await repository.SaveChangesAsync(ct);
            return Result.Success<string?>(null); // görsel elle yüklenecek
        }

        try
        {
            var title = item.Revisions.FirstOrDefault(r => r.IsCurrent)?.Title ?? item.RawTitle ?? "Başlık";
            var (url, kind, w, h) = await BuildImageAsync(source, title, ct);
            item.AddMedia(kind, url, w, h, titleBurned: kind == MediaKind.SkiaCard, clock);
            item.MarkMediaReady(clock);
            await repository.SaveChangesAsync(ct);
            return Result.Success<string?>(url);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Önizleme görseli üretilemedi: {Id}", contentItemId);
            return Result.Failure<string?>(Error.Conflict("Görsel üretilemedi: " + ex.Message));
        }
    }

    /// <summary>Detay ekranından yüklenen görseli ekler — YAYINLAMAZ (önizleme/hazırlık). Yüklenen görselin URL'sini döner.</summary>
    public async Task<Result<string>> AttachPreviewImageAsync(Guid contentItemId, byte[] bytes, string contentType, CancellationToken ct)
    {
        var item = await repository.GetAsync(contentItemId, ct);
        if (item is null) return Result.Failure<string>(Error.NotFound("İçerik"));

        var url = await mediaStore.SaveAsync(bytes, contentType, ct);
        item.SetImageSource(ImageSource.Manual, clock);
        item.AddMedia(MediaKind.Manual, url, 0, 0, titleBurned: false, clock);
        item.MarkMediaReady(clock);
        await repository.SaveChangesAsync(ct);
        return Result.Success(url);
    }

    /// <summary>AI görsel istemi: gerçekçi + dikkat çekici editoryal kapak; başlık tasarıma şık gömülür.</summary>
    private static string BuildAiImagePrompt(string title) =>
        $"Konusu \"{title}\" olan bir haber/blog yazisi icin GERCEKCI, YUKSEK KALITE, dikkat cekici bir KAPAK gorseli uret. " +
        "Tarz: foto-gercekci ya da sinematik premium dijital sanat; DUZ/BASIT/KLISE VEKTOR veya kroki cizim DEGIL. " +
        "Sinematik aydinlatma, alan derinligi, zengin doku ve detay; insanlarin tiklamak isteyecegi carpici bir kompozisyon (sosyal medya thumbnail estetigi). " +
        $"Gorselin uzerine, tasarima SIK bir sekilde entegre, BUYUK ve OKUNAKLI modern bir tipografiyle su baslik DOGRU sekilde yazilsin: \"{title}\". " +
        "Baslik net, hatasiz, gorsel hiyerarside one cikan bir manset gibi olsun; okunabilirlik icin gerekli yerde hafif koyu zemin/gradyan kullan.";

    private async Task<(string Url, MediaKind Kind, int W, int H)> BuildImageAsync(ImageSource source, string title, CancellationToken ct)
    {
        if (source == ImageSource.Ai)
        {
            try
            {
                var img = await imageProvider.GenerateAsync(
                    new ImageGenerationRequest(BuildAiImagePrompt(title), 1024, 1024, "high"), ct);
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

    /// <summary>Kalite kapısı: kritik sorun varsa içeriği incelemeye alır (yayınlamaz); yoksa yayına-hazır olayı.</summary>
    private async Task MaybePublishAsync(ContentItem item, Fields f, string? mediaUrl, CancellationToken ct)
    {
        if (item.UseAi)
        {
            var q = qualityGate.Evaluate(f.Title, f.ShortX, f.BodyHtml, f.Tags.ToList());
            if (q.Critical)
            {
                var reason = string.Join(" ", q.Issues);
                item.HoldForReview("Kalite kapısı: " + reason, clock);
                audit.Log(item.Id, AuditEvent.HeldForReview, ActorType.System, "quality-gate", reason);
                await repository.SaveChangesAsync(ct);
                logger.LogInformation("İçerik kalite kapısında tutuldu (incelemeye): {Id} — {Issues}", item.Id, reason);
                return;
            }
        }
        await PublishReadyAsync(item, f, mediaUrl, ct);
    }

    /// <summary>Admin override: tutulmuş/hazır bir içeriği elle yayına gönderir (kalite kapısını atlar).</summary>
    public async Task<Result> PublishExistingAsync(Guid contentItemId, CancellationToken ct)
    {
        var item = await repository.GetAsync(contentItemId, ct);
        if (item is null) return Result.Failure(Error.NotFound("İçerik"));
        var rev = item.Revisions.FirstOrDefault(r => r.IsCurrent);
        if (rev is null) return Result.Failure(Error.Conflict("Yayınlanacak güncel revizyon yok."));
        if (item.MediaStatus != MediaStatus.Ready) return Result.Failure(Error.Conflict("Görsel hazır değil (önce üretim/yükleme)."));
        var mediaUrl = item.Media.LastOrDefault()?.Url;
        await PublishReadyAsync(item,
            new Fields(rev.Title, rev.ShortX, rev.BodyHtml, rev.InstagramCaption, rev.Tags, rev.PrimaryKeyword, rev.ImageAltText),
            mediaUrl, ct);
        return Result.Success();
    }

    private Task PublishReadyAsync(ContentItem item, Fields f, string? mediaUrl, CancellationToken ct)
    {
        // Blog linki: test içeriği bloglanmaz → linksiz. Aksi halde Site ile AYNI slug formülü (BlogSlug).
        string? link = null;
        var baseUrl = siteOptions.Value.BaseUrlTrimmed;
        if (!item.TestMode && baseUrl.Length > 0)
            link = $"{baseUrl}/blog/{BlogSlug.Build(item.Id, f.PrimaryKeyword, f.Title)}";

        return bus.PublishAsync(new ContentReadyToPublishIntegrationEvent(
            Guid.NewGuid(), clock.UtcNow, item.Id, item.CategoryId, item.TestMode,
            f.Title, f.ShortX, f.BodyHtml, f.InstagramCaption, f.Tags.ToList(), f.PrimaryKeyword, mediaUrl,
            Link: link, ScheduledAt: item.ScheduledAt), ct);
    }

    // ---- Elle (panel) AI üretimi ----

    /// <summary>Panelden bir içeriğin TÜM alanlarını AI ile üretir (yeni revizyon). Görsel/yayın yapmaz — önizleme/düzenleme için.</summary>
    public async Task<Result> GenerateDraftAsync(Guid contentItemId, string? seedInput, CancellationToken ct)
    {
        if (await killSwitch.IsAiStoppedAsync(null, ct))
            return Result.Failure(Error.Conflict("AI üretimi durdurulmuş (kill-switch)."));

        var item = await repository.GetAsync(contentItemId, ct);
        if (item is null) return Result.Failure(Error.NotFound("İçerik"));

        var input = !string.IsNullOrWhiteSpace(seedInput) ? seedInput! : $"Baslik: {item.RawTitle}\n\nMetin: {item.RawInput}";
        try
        {
            var f = await RunTextAsync(input, item.RawTitle, ct);
            var nextNo = (item.Revisions.Count == 0 ? 0 : item.Revisions.Max(r => r.RevisionNumber)) + 1;
            item.AddRevision(new ContentRevision(item.Id, nextNo, f.Title, f.ShortX, f.BodyHtml,
                f.InstagramCaption, f.Tags.ToList(), f.PrimaryKeyword, f.ImageAltText, createdBy: "ai-manual", clock));
            audit.Log(item.Id, AuditEvent.Generated, ActorType.AdminUser, "admin", "Panelden AI ile üretildi");
            await repository.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (DbUpdateException ex)
        {
            var detail = ex.InnerException?.Message ?? ex.Message;
            logger.LogWarning(ex, "Üretim kaydı DB hatası: {Id}", contentItemId);
            return Result.Failure(Error.Conflict("Kayıt hatası: " + detail));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Panelden AI üretimi başarısız: {Id}", contentItemId);
            return Result.Failure(Error.Conflict("AI üretimi başarısız: " + ex.Message));
        }
    }

    /// <summary>Tek bir alanı verilen bağlamdan üretir (KAYDETMEZ; değeri döndürür). Panelde "✨ AI" düğmeleri.</summary>
    public async Task<Result<string>> GenerateFieldAsync(GenerateFieldRequest req, CancellationToken ct)
    {
        if (await killSwitch.IsAiStoppedAsync(null, ct))
            return Result.Failure<string>(Error.Conflict("AI üretimi durdurulmuş (kill-switch)."));

        var system = FieldPrompt(req.Field);
        if (system is null) return Result.Failure<string>(Error.Validation("Bilinmeyen alan: " + req.Field));

        var context = $"Başlık: {req.Title}\n\nKısa (X): {req.ShortX}\n\nAna metin (HTML): {req.BodyHtml}\n\nInstagram: {req.InstagramCaption}\n\nKaynak/not: {req.RawInput}";
        try
        {
            var result = await textProvider.GenerateAsync(new TextGenerationRequest(system, context, "tr", "v1"), ct);
            return Result.Success(CleanPlain(result.RawJson));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Alan üretimi başarısız: {Field}", req.Field);
            return Result.Failure<string>(Error.Conflict("AI üretimi başarısız: " + ex.Message));
        }
    }

    private async Task<Fields> RunTextAsync(string input, string? fallbackTitle, CancellationToken ct)
    {
        var result = await textProvider.GenerateAsync(new TextGenerationRequest(SystemPrompt, input, "tr", "v1"), ct);
        return ParseFields(result.RawJson, fallbackTitle ?? "Baslik");
    }

    private static string? FieldPrompt(string field) => field switch
    {
        "title" => "Aşağıdaki içerikten TÜRKÇE, özgün, SOMUT, SEO uyumlu, tıklanır YENİ bir başlık üret. Kaynağın başlığını aynen kullanma/birebir çevirme AMA kaynaktaki asıl konuyu yansıt (gerekiyorsa kişi/kurum adı geçebilir); genel/klişe olmasın; ana anahtar kelimeyi içersin, ~50-65 karakter. Kaynak İngilizce olsa bile çıktı Türkçe olsun. YALNIZCA başlığı düz metin döndür; tırnak/açıklama/JSON yok.",
        "shortX" => "Aşağıdaki içerikten X (Twitter) için TÜRKÇE, EN FAZLA 280 karakter, TEK BAŞINA anlaşılır NET bir ÖZET üret: kim + ne dedi/ne oldu açık olsun (kişi/kurum + iddia). Kaynakta bir kişi/kuruma atıf varsa koru, 'uzmanlar' gibi belirsizleştirme; sitenin kendi görüşü gibi yazma. Belirsiz teaser değil (gerekirse 1-2 hashtag). YALNIZCA metni döndür.",
        "bodyHtml" => "Aşağıdaki kaynağı TÜRKÇE, özgün, telifsiz ve SEO'ya uygun UZUNLUKTA (en az 600, mümkünse 700-1000 kelime) bir blog gövdesi olarak AKTAR. Kaynaktaki kişi/kurum atıflarını koru ('uzmanlar' gibi belirsizleştirme, sitenin görüşü gibi sunma); uydurma bilgi ekleme. En az 2-3 <h2> (gerekirse <h3>) alt başlık kullan; her paragrafı <p>...</p> ile sar; ana anahtar kelimeyi başlıkta ve ilk paragrafta doğal kullan. Kaynak İngilizce olsa bile çıktı Türkçe olsun. YALNIZCA HTML gövdeyi döndür.",
        "instagramCaption" => "Aşağıdaki içerikten Instagram için TÜRKÇE, EN FAZLA 2200 karakter, TEK BAŞINA anlaşılır akıcı bir özet üret: kim/ne/neden açık olsun; kaynaktaki kişi/kurum atfını koru (uygunsa hashtag). YALNIZCA metni döndür.",
        "tags" => "Aşağıdaki içerik için TÜRKÇE 3-8 kısa SEO etiketi üret. YALNIZCA etiketleri virgülle ayırarak döndür (ör: kripto, bitcoin, regülasyon).",
        _ => null
    };

    /// <summary>Model bazen kod bloğu/tırnakla sarar — düz metne indir.</summary>
    private static string CleanPlain(string s)
    {
        s = s.Trim();
        if (s.StartsWith("```"))
        {
            var nl = s.IndexOf('\n');
            if (nl >= 0) s = s[(nl + 1)..];
            var fence = s.LastIndexOf("```", StringComparison.Ordinal);
            if (fence >= 0) s = s[..fence];
        }
        return s.Trim().Trim('"').Trim();
    }

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
