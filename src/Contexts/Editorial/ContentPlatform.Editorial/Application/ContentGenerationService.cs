using System.Text.Json;
using ContentPlatform.Abstractions;
using ContentPlatform.Editorial.Contracts;
using ContentPlatform.Editorial.Domain;
using ContentPlatform.Editorial.Infrastructure;
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
    ISlideVideoRenderer videoRenderer,
    IMediaStore mediaStore,
    IMediaReader mediaReader,
    ISettingsProvider settingsProvider,
    IIntegrationEventPublisher bus,
    IQualityGate qualityGate,
    IContentAudit audit,
    IArticleTextExtractor articleExtractor,
    IPublicCategoryProvider categoryProvider,
    IKillSwitch killSwitch,
    IClock clock,
    IOptions<SiteOptions> siteOptions,
    IOptions<MediaOptions> mediaOptions,
    ILogger<ContentGenerationService> logger)
{

    private const string SystemPrompt =
        "Sen SEO odakli bir HABER/icerik editorusun. Kaynak metni Turkce, ozgun, telifsiz ve ARAMA MOTORU DOSTU sekilde AKTAR; kaynakta olmayan isim/veri/bilgi UYDURMA. " +
        "DIL (COK ONEMLI): Kaynak hangi dilde olursa olsun (Ingilizce dahil) TUM CIKTIYI TURKCE uret. " +
        "ATIF / SADAKAT (COK ONEMLI, HER SENARYO): Kaynaktaki KIM-NE-NEREDE bilgisini KORU. Bir gorus/iddia kaynakta bir kisiye veya kuruma atfedilmisse (ornek: 'X, Y kurumu CEO'su'), icerikte de MUTLAKA o kisi/kuruma atfet; 'uzmanlar/yetkililer' gibi BELIRSIZ ifadeye cevirme ve sitenin kendi gorusuymus gibi SUNMA. Kaynakta acik bir kisi/kurum atfi YOKSA, atif UYDURMA (hayali 'uzman/yetkili' yazma); olayi tarafsiz haber diliyle, YALNIZCA kaynaktaki bilgiyle sinirli aktar. Sen yorum yapmiyorsun, kaynagi aktariyorsun. Bu kurallar her tur icerik icin gecerlidir: haber, duyuru, analiz, gorus, rehber. " +
        "OLGU CIKARIMI (COK ONEMLI): Yazmaya baslamadan once kaynaktaki TUM SOMUT OLGULARI cikar: sayilar, tutarlar, yuzdeler, tarihler, takvimler, adetler, kisi/kurum/yer adlari, plan/pilot ayrintilari, alinan kararlar. Bu olgularin MUMKUN OLDUGUNCA HEPSINI govdeye dogal bicimde YERLESTIR — somut olgu atlanmis haber EKSIK haberdir. Kaynak birden cok neden/katman/madde sayiyorsa (ornegin 'uc katmanli tehdit') HEPSINI tek tek acikca yaz, ozetleyip gecme. " +
        "META CUMLE YASAK: 'kaynak metin belirtmiyor / aciklamiyor / bilgi verilmemis' gibi KAYNAGA GONDERME yapan cumleler ASLA YAZMA — okuyucu kaynagi bilmez, bu cumleler anlamsizdir. Bilgi kaynakta gercekten yoksa o konuyu HIC ACMA; varsa dogru ve eksiksiz aktar. Kaynakta OLAN bir bilgiye 'belirtilmemis' demek AGIR HATADIR. " +
        "TEKRAR YASAK: Ayni fikri/cumleyi farkli kelimelerle TEKRARLAMA. Her paragraf YENI bir bilgi, ayrinti, boyut ya da baglam eklemeli; iki fikri on paragrafa YAYMA. Yazacak yeni bilgin bittiyse metni BITIR. " +
        "UZUNLUK: Hedef 600-1000 kelime; AMA kaynaktaki bilgi bu uzunlugu doldurmuyorsa TEKRARLA SISIRME — kaynaktaki tum olgulari kullandiktan sonra dogal uzunlukta bitir. Dolu 400 kelime, tekrarli 800 kelimeden HER ZAMAN iyidir. " +
        "TELIF/KURGU: Kaynagin cumlelerini birebir cevirme; paragraf sirasini ve kurgusunu IZLEME — olgulari onem sirasina gore KENDI kurgunla yeniden duzenle (ters piramit: en onemli olgu ve olay giriste). 7 kelimeyi asan birebir alinti KULLANMA. Bu kural LISTE/SIRALAMA cumleleri icin de gecerli: kaynak konu/ozellik siraliyorsa ogeleri AYNI SIRAYLA ve ayni kelime dizilimiyle aktarma — sirayi degistir, es anlamli ifadelerle yeniden kur, gerekirse listeyi boler farkli cumlelere dagit. (Fiyat, tarih, resmi kategori/urun adi gibi degistirilemez olgusal kaliplar bunun DISINDADIR — onlari aynen koru.) Kaynagin alt basligindaki tek fikri genisletmek yerine metnin TAMAMINDAKI olgulari isle. " +
        "BASLIK-GOVDE UYUMU: Basligin vaat ettigi konu govdenin AGIRLIK MERKEZI olmali; baslikta A konusu deyip govdeyi B konusuna KAYDIRMA. " +
        "BASLIK: Kaynagin basligini AYNEN KULLANMA/birebir cevirme; ama kaynaktaki ASIL KONUYU yansit (gerekiyorsa kisi/kurum adi gecebilir). Turkce, ozgun, SOMUT, ilgi cekici, SEO uyumlu; ana anahtar kelimeyi icersin; ~50-65 karakter. Genel/klise baslik yazma. " +
        "shortX (X/Twitter): <=280 karakter, TEK BASINA anlasilir NET bir OZET olsun — kim + ne dedi/ne oldu acik (kisi/kurum + iddia). Belirsiz teaser DEGIL; tweet tek basina okununca olay anlasilmali. " +
        "instagramCaption: <=2200 karakter, TEK BASINA anlasilir akici bir ozet; kim/ne/neden acik; kaynaktaki atfi koru. " +
        "GOVDE (bodyHtml): TURKCE; uzunluk icin UZUNLUK kuralina uy (hedef 600-1000, ama tekrarla/dolguyla SISIRME). Kaynagin GERCEK konusuna odaklan; genel dolgu/klise yok; kaynaktaki somut olgularin tamamini kullan. " +
        "Yapilandir: icerik YETERLIYSE 2-3 adet <h2> (gerekirse <h3>) alt baslik — alt basliklar FARKLI bilgi bolumlerini ayirmali; ince kaynakta ayni fikri bolup SAHTE alt baslik uretme (alt bassiz kisa haber de gecerli). Her paragrafi <p>...</p> ile sar; paragraflar ARASINDA gercek cift satir sonu (\\n\\n) birak, tek paragrafta BIRLESTIRME. " +
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
                    // Girdi: kaynak sayfadan çıkarılan TAM makale metni (varsa) — yoksa RSS özeti.
                    var input = await BuildAiInputAsync(item, ct);
                    f = await RunTextAsync(input, item.RawTitle, allowPartial: false, ct); // otomatik hat: TAM içerik şart
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

                string? mediaUrl;
                if (item.MediaStatus == MediaStatus.Ready && LatestImageUrl(item) is { } readyUrl)
                {
                    // Görsel zaten (önizlemeden) üretilmiş/yüklenmiş — yeniden üretme, mevcutla yayına gönder.
                    // (Video medyası GÖRSEL sayılmaz — LatestImageUrl videoyu atlar.)
                    mediaUrl = readyUrl;
                }
                else
                {
                    var (url, kind, w, h) = await BuildImageAsync(item.ImageSource, f.Title, null, ct);
                    item.AddMedia(kind, url, w, h, titleBurned: kind == MediaKind.SkiaCard, clock);
                    item.MarkMediaReady(clock);
                    mediaUrl = url;
                }
                await repository.SaveChangesAsync(ct);

                await MaybePublishAsync(item, f, mediaUrl, ct);
                produced++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "AI uretimi basarisiz: {Id}", item.Id);
                // Hatayi icerige yaz ki panelde 'HoldReason' olarak gorunur olsun (sessiz kalmasin).
                try { item.HoldForReview("Üretim hatası: " + ex.Message, clock); await repository.SaveChangesAsync(ct); }
                catch (Exception ex2) { logger.LogWarning(ex2, "Üretim hatası içeriğe yazılamadı: {Id}", item.Id); }
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

        var (optBytes, optType) = OptimizeUpload(bytes, contentType, logger);
        var url = await mediaStore.SaveAsync(optBytes, optType, ct);
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
    public async Task<Result<string?>> GeneratePreviewImageAsync(Guid contentItemId, ImageSource source, int? cardStyle, CancellationToken ct)
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
            // SkiaCard için: seçili şablon varsa onu, yoksa RASTGELE (her basışta farklı tasarım). AI'da yok sayılır.
            var theme = cardStyle is { } cs ? cs.ToString() : System.Random.Shared.Next(0, 24).ToString();
            // Panelden AI seçildiyse SESSİZCE SkiaCard'a düşme: gerçek hata (anahtar/model/bakiye)
            // toast'ta görünsün ki kullanıcı sorunu çözebilsin. Otomatik akıştaki fallback aynen durur.
            var (url, kind, w, h) = await BuildImageAsync(source, title, theme, ct, aiStrict: source == ImageSource.Ai);
            item.AddMedia(kind, url, w, h, titleBurned: kind == MediaKind.SkiaCard, clock);
            item.MarkMediaReady(clock);
            await repository.SaveChangesAsync(ct);
            await PublishIfApprovedAsync(item, url, ct); // onaylı içerik burada takılıp kalmasın
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

        var (optBytes, optType) = OptimizeUpload(bytes, contentType, logger);
        var url = await mediaStore.SaveAsync(optBytes, optType, ct);
        item.SetImageSource(ImageSource.Manual, clock);
        item.AddMedia(MediaKind.Manual, url, 0, 0, titleBurned: false, clock);
        item.MarkMediaReady(clock);
        await repository.SaveChangesAsync(ct);
        await PublishIfApprovedAsync(item, url, ct); // onaylı içerik burada takılıp kalmasın
        return Result.Success(url);
    }

    /// <summary>
    /// Kullanıcının yüklediği görseli GÖRSEL KALİTEYİ BOZMADAN küçültür (disk + sayfa hızı):
    ///  - En uzun kenar 1600px'i aşıyorsa yüksek kaliteli (Mitchell cubic) yeniden örnekleme ile 1600'e iner
    ///    (sitede kapak ~1200px, Instagram 1080px ister → 1600 her kullanım için fazlasıyla yeterli).
    ///  - JPEG kalite 85 ile kodlanır (görsel olarak kayıpsız kabul edilen eşik).
    ///  - Sonuç orijinalden BÜYÜK çıkarsa orijinal korunur (zaten optimize dosya şişirilmez).
    ///  - Çözülemeyen/bozuk dosyada orijinal aynen kaydedilir (yükleme asla bu yüzden patlamaz).
    /// </summary>
    internal static (byte[] Bytes, string ContentType) OptimizeUpload(byte[] bytes, string contentType, ILogger? log = null)
    {
        try
        {
            using var bmp = SkiaSharp.SKBitmap.Decode(bytes);
            if (bmp is null) return (bytes, contentType);

            const int MaxDim = 1600;
            var maxSide = Math.Max(bmp.Width, bmp.Height);
            SkiaSharp.SKBitmap final = bmp;
            if (maxSide > MaxDim)
            {
                var scale = MaxDim / (float)maxSide;
                var nw = Math.Max(1, (int)Math.Round(bmp.Width * scale));
                var nh = Math.Max(1, (int)Math.Round(bmp.Height * scale));
                final = bmp.Resize(new SkiaSharp.SKImageInfo(nw, nh),
                    new SkiaSharp.SKSamplingOptions(SkiaSharp.SKCubicResampler.Mitchell)) ?? bmp;
            }

            using var img = SkiaSharp.SKImage.FromBitmap(final);
            using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 85);
            var jpeg = data.ToArray();
            if (!ReferenceEquals(final, bmp)) final.Dispose();

            if (jpeg.Length >= bytes.Length) return (bytes, contentType); // küçülmediyse dokunma
            log?.LogInformation("Yüklenen görsel optimize edildi: {Once} KB → {Sonra} KB",
                bytes.Length / 1024, jpeg.Length / 1024);
            return (jpeg, "image/jpeg");
        }
        catch { return (bytes, contentType); }
    }

    /// <summary>AI görsel istemi: gerçekçi + dikkat çekici editoryal kapak; başlık tasarıma şık gömülür.</summary>
    /// <summary>Video arka planı: YAZISIZ, dikey, üstüne açık renk metin basılınca okunaklı kalacak sade görsel.</summary>
    private static string BuildVideoBackgroundPrompt(string title) =>
        $"Konusu \"{title}\" olan bir haber icin DIKEY (9:16) Reels/Shorts video ARKA PLAN gorseli uret. " +
        "UZERINDE HICBIR YAZI, HARF, RAKAM veya LOGO OLMASIN — metin sonradan ustune basilacak. " +
        "Foto-gercekci ya da sinematik dijital sanat; atmosferik, koyu tonlarin agir bastigi, sade ve derinlikli bir kompozisyon; " +
        "merkez bolge gorece sakin kalsin ki ustune basilacak acik renk yazi OKUNAKLI olsun; asiri karmasik/detayli olmasin.";

    private static string BuildAiImagePrompt(string title) =>
        $"Konusu \"{title}\" olan bir haber/blog yazisi icin GERCEKCI, YUKSEK KALITE, dikkat cekici bir KAPAK gorseli uret. " +
        "Tarz: foto-gercekci ya da sinematik premium dijital sanat; DUZ/BASIT/KLISE VEKTOR veya kroki cizim DEGIL. " +
        "Sinematik aydinlatma, alan derinligi, zengin doku ve detay; insanlarin tiklamak isteyecegi carpici bir kompozisyon (sosyal medya thumbnail estetigi). " +
        $"Gorselin uzerine, tasarima SIK bir sekilde entegre, BUYUK ve OKUNAKLI modern bir tipografiyle su baslik DOGRU sekilde yazilsin: \"{title}\". " +
        "Baslik net, hatasiz, gorsel hiyerarside one cikan bir manset gibi olsun; okunabilirlik icin gerekli yerde hafif koyu zemin/gradyan kullan.";

    /// <summary>aiStrict=true (panel önizlemesi): AI başarısızsa SkiaCard'a DÜŞMEZ, hata fırlatır —
    /// kullanıcı bilinçli AI seçtiyse gerçek sebebi (anahtar/model erişimi/bakiye) görmelidir.
    /// aiStrict=false (otomatik hat): eski davranış — görselsiz makale kalmasın diye SkiaCard'a düşer.</summary>
    private async Task<(string Url, MediaKind Kind, int W, int H)> BuildImageAsync(ImageSource source, string title, string? cardTheme, CancellationToken ct, bool aiStrict = false)
    {
        var (cardW, cardH) = CardSize();
        if (source == ImageSource.Ai)
        {
            var (aiW, aiH) = AiImageSize(cardW, cardH);
            try
            {
                // Varsayılan kalite "medium": "high" 2-3+ dk sürüp istemci zaman aşımına takılabiliyor;
                // medium ~30-60 sn ve kalite farkı küçük. Panelden OpenAI:ImageQuality=high ile yükseltilebilir.
                var img = await imageProvider.GenerateAsync(
                    new ImageGenerationRequest(BuildAiImagePrompt(title), aiW, aiH, "medium"), ct);
                if (img.Bytes.Length > 0)
                {
                    var aiUrl = await mediaStore.SaveAsync(img.Bytes, img.ContentType, ct);
                    return (aiUrl, MediaKind.AiImage, aiW, aiH);
                }
                if (aiStrict) throw new InvalidOperationException("AI görsel servisi boş görsel döndürdü.");
            }
            catch (Exception ex) when (!aiStrict)
            {
                logger.LogWarning(ex, "AI gorsel basarisiz, SkiaCard'a dusuluyor.");
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                throw new InvalidOperationException("AI görsel üretilemedi: " + FriendlyAiError(ex), ex);
            }
        }

        var card = cardRenderer.RenderTitleCard(title, cardTheme, cardW, cardH);
        var url = await mediaStore.SaveAsync(card, "image/png", ct);
        return (url, MediaKind.SkiaCard, cardW, cardH);
    }

    /// <summary>Kart boyutu config'ten (Media:CardWidth/Height) — Instagram/X/Telegram için ORTAK boyut.</summary>
    private (int W, int H) CardSize()
    {
        var o = mediaOptions.Value;
        return (o.CardWidth > 0 ? o.CardWidth : 1080, o.CardHeight > 0 ? o.CardHeight : 1080);
    }

    /// <summary>Kart oranına en yakın OpenAI görsel boyutu — kart ile AI görsel AYNI oranda kalsın.</summary>
    private static (int W, int H) AiImageSize(int cardW, int cardH)
    {
        var ratio = (double)cardW / cardH;
        if (ratio > 1.15) return (1536, 1024);  // yatay
        if (ratio < 0.87) return (1024, 1536);  // dikey
        return (1024, 1024);                    // kare
    }

    /// <summary>
    /// İçerik ZATEN onaylıyken görsel sonradan hazır olursa yayını tetikler. (Üretim sorgusu
    /// "Ready medya + güncel revizyon" içeriği almaz; bu çağrı olmasa içerik sessizce takılırdı.)
    /// </summary>
    private async Task PublishIfApprovedAsync(ContentItem item, string? mediaUrl, CancellationToken ct)
    {
        if (item.EditorialStatus != EditorialStatus.Approved) return;
        var rev = item.Revisions.FirstOrDefault(r => r.IsCurrent);
        if (rev is null) return; // metin yok → PipelineDrainJob metni üretince yayınlar
        await MaybePublishAsync(item,
            new Fields(rev.Title, rev.ShortX, rev.BodyHtml, rev.InstagramCaption, rev.Tags, rev.PrimaryKeyword, rev.ImageAltText),
            mediaUrl, ct);
    }

    /// <summary>Kalite kapısı: kritik sorun varsa içeriği incelemeye alır (yayınlamaz); yoksa yayına-hazır olayı.</summary>
    private async Task MaybePublishAsync(ContentItem item, Fields f, string? mediaUrl, CancellationToken ct)
    {
        if (item.UseAi)
        {
            var q = qualityGate.Evaluate(f.Title, f.ShortX, f.BodyHtml, f.Tags.ToList(), item.RawInput);
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
    public async Task<Result> PublishExistingAsync(Guid contentItemId, bool adGate, CancellationToken ct)
    {
        var item = await repository.GetAsync(contentItemId, ct);
        if (item is null) return Result.Failure(Error.NotFound("İçerik"));
        var rev = item.Revisions.FirstOrDefault(r => r.IsCurrent);
        if (rev is null) return Result.Failure(Error.Conflict("Yayınlanacak güncel revizyon yok."));
        if (item.MediaStatus != MediaStatus.Ready) return Result.Failure(Error.Conflict("Görsel hazır değil (önce üretim/yükleme)."));
        var mediaUrl = LatestImageUrl(item); // video medyası görsel olarak gitmesin
        if (mediaUrl is null) return Result.Failure(Error.Conflict("Görsel yok — yalnız video ile yayına alınamaz; önce görsel üret/yükle."));
        await PublishReadyAsync(item,
            new Fields(rev.Title, rev.ShortX, rev.BodyHtml, rev.InstagramCaption, rev.Tags, rev.PrimaryKeyword, rev.ImageAltText),
            mediaUrl, ct, adGate);
        return Result.Success();
    }

    private Task PublishReadyAsync(ContentItem item, Fields f, string? mediaUrl, CancellationToken ct, bool adGate = false)
    {
        // NORMAL yayın akışı ARTIK test bayrağından bağımsız: içerik test'e gönderilmiş olsa bile
        // zamanı gelince GERÇEK hedeflere planlanır/yayınlanır ve bloga girer. Test, ayrı ve tek
        // seferlik bir aksiyondur (SendTestAsync) — içeriği asıl akıştan ÇIKARMAZ.
        string? link = null;
        var baseUrl = siteOptions.Value.BaseUrlTrimmed;
        if (baseUrl.Length > 0)
            link = $"{baseUrl}/blog/{BlogSlug.Build(item.Id, f.PrimaryKeyword, f.Title)}";

        return bus.PublishAsync(new ContentReadyToPublishIntegrationEvent(
            Guid.NewGuid(), clock.UtcNow, item.Id, item.CategoryId, TestMode: false,
            f.Title, f.ShortX, f.BodyHtml, f.InstagramCaption, f.Tags.ToList(), f.PrimaryKeyword, mediaUrl,
            Link: link, ScheduledAt: item.ScheduledAt, AdGate: adGate, VideoUrl: LatestVideoUrl(item)), ct);
    }

    /// <summary>
    /// İçeriği TEST hedeflerine TEK SEFERLİK, HEMEN gönderir (kadans/plan yok, blog yok).
    /// İçeriğin asıl yayın akışını DEĞİŞTİRMEZ — normal planlama/yayın aynen devam eder.
    /// </summary>
    public async Task<Result> SendTestAsync(Guid contentItemId, CancellationToken ct)
    {
        var item = await repository.GetAsync(contentItemId, ct);
        if (item is null) return Result.Failure(Error.NotFound("İçerik"));
        var rev = item.Revisions.FirstOrDefault(r => r.IsCurrent);
        if (rev is null) return Result.Failure(Error.Conflict("Gönderilecek güncel revizyon yok (önce üret/kaydet)."));
        if (item.MediaStatus != MediaStatus.Ready) return Result.Failure(Error.Conflict("Görsel hazır değil (önce üret/yükle)."));
        var mediaUrl = LatestImageUrl(item); // video medyası görsel olarak gitmesin

        await bus.PublishAsync(new ContentReadyToPublishIntegrationEvent(
            Guid.NewGuid(), clock.UtcNow, item.Id, item.CategoryId, TestMode: true,
            rev.Title, rev.ShortX, rev.BodyHtml, rev.InstagramCaption, rev.Tags.ToList(), rev.PrimaryKeyword, mediaUrl,
            Link: null, ScheduledAt: null, AdGate: false, VideoUrl: LatestVideoUrl(item)), ct);
        return Result.Success();
    }

    /// <summary>Son GÖRSEL (video hariç) medya URL'i — ÜRETİM ZAMANINA göre en yenisi.
    /// (Listenin sırasına güvenilmez: EF koleksiyonu DB'den Guid anahtar sırasıyla, yani RASTGELE
    /// yükler — SkiaCard'dan SONRA üretilen AI görseli listede önde kalıp seçilmeyebiliyordu.)</summary>
    private static string? LatestImageUrl(ContentItem item) =>
        item.Media.Where(m => m.Kind != MediaKind.Video).OrderBy(m => m.CreatedAt).LastOrDefault()?.Url;

    /// <summary>Son VİDEO medya URL'i (Reels/Shorts) — üretim zamanına göre en yenisi.</summary>
    private static string? LatestVideoUrl(ContentItem item) =>
        item.Media.Where(m => m.Kind == MediaKind.Video).OrderBy(m => m.CreatedAt).LastOrDefault()?.Url;

    /// <summary>
    /// Reels/Shorts/TikTok için slayt videosu üretir (X metni → 3 sayfa × 7 sn = 21 sn, 1080x1920).
    /// YAYINLAMAZ — önizleme; yayında IG/YouTube/TikTok'a video varsa video gider, yoksa görsel.
    /// </summary>
    public async Task<Result<string?>> GeneratePreviewVideoAsync(Guid contentItemId, int? style, bool aiBackground, CancellationToken ct)
    {
        var item = await repository.GetAsync(contentItemId, ct);
        if (item is null) return Result.Failure<string?>(Error.NotFound("İçerik"));

        var rev = item.Revisions.FirstOrDefault(r => r.IsCurrent);
        var title = rev?.Title ?? item.RawTitle ?? "Başlık";
        var text = rev?.ShortX;
        if (string.IsNullOrWhiteSpace(text))
            return Result.Failure<string?>(Error.Conflict("Video için X metni gerekli — önce metni üret/kaydet."));

        try
        {
            // AI ARKA PLAN (opsiyonel, panelden "AI görselli video"): habere uygun DİKEY görsel üretilir,
            // renderer bunu cover-crop edip üstüne koyu katman basar. Başarısızsa hata GÖSTERİLİR
            // (sessizce şablona düşülmez — kullanıcı bilinçli AI seçti). Şablonlu üretim aynen ayrı yol.
            byte[]? bgImage = null;
            if (aiBackground)
            {
                var img = await imageProvider.GenerateAsync(
                    new ImageGenerationRequest(BuildVideoBackgroundPrompt(title), 1024, 1536, "medium"), ct); // hız: bkz. BuildImageAsync notu
                if (img.Bytes.Length == 0)
                    return Result.Failure<string?>(Error.Conflict("AI arka plan görseli üretilemedi (boş yanıt)."));
                bgImage = img.Bytes;
            }

            // Arka plan müziği (panelden yüklenen mp3 — video.music_url). Yoksa sessiz video.
            byte[]? music = null;
            var musicUrl = await settingsProvider.GetAsync("video.music_url", ct);
            if (!string.IsNullOrWhiteSpace(musicUrl))
                music = (await mediaReader.TryReadAsync(musicUrl!, ct))?.Bytes;

            // Kategori adı slaytlara basılır (ne haberi olduğu anlaşılsın diye).
            string? category = null;
            if (item.CategoryId is { } catId)
                category = (await categoryProvider.GetActiveAsync(ct)).FirstOrDefault(c => c.Id == catId)?.Name;

            var bytes = await videoRenderer.RenderSlidesVideoAsync(title, text!, music, style, category, bgImage, ct);
            var url = await mediaStore.SaveAsync(bytes, "video/mp4", ct);
            var o = mediaOptions.Value;
            item.AddMedia(MediaKind.Video, url, o.VideoWidth > 0 ? o.VideoWidth : 1080,
                o.VideoHeight > 0 ? o.VideoHeight : 1920, titleBurned: true, clock);
            // GÖRSEL yoksa içerik yayına GİREMEZ (video tek başına yetmez) → "Görsel Bekleyenler"de görünür,
            // kaybolmaz. Görsel üretilince/yüklenince normal akış devam eder.
            if (LatestImageUrl(item) is null && item.MediaStatus != MediaStatus.Ready)
                item.MarkAwaitingManualImage(clock);
            await repository.SaveChangesAsync(ct);
            audit.Log(item.Id, AuditEvent.Generated, ActorType.AdminUser, "admin", "Slayt videosu üretildi");
            return Result.Success<string?>(url);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Video üretilemedi: {Id}", contentItemId);
            return Result.Failure<string?>(Error.Conflict("Video üretilemedi: " + ex.Message));
        }
    }

    /// <summary>
    /// AI üretim girdisini kurar: SourceUrl varsa (RSS/ingestion) kaynak sayfadan OKUNUR makale metni
    /// çıkarılır ve o gönderilir (kaynak kodu değil → token israfı yok, içerik zengin). Çıkarılamazsa
    /// RSS özetine (RawInput) düşülür. Özgünlük talimatı eklenir — kaynaktan cümle kopyalanmaz.
    /// </summary>
    private async Task<string> BuildAiInputAsync(ContentItem item, CancellationToken ct)
    {
        var fallback = $"Baslik: {item.RawTitle}\n\nMetin: {item.RawInput}";
        if (string.IsNullOrWhiteSpace(item.SourceUrl)) return fallback;

        var article = await articleExtractor.ExtractAsync(item.SourceUrl!, ct);
        if (string.IsNullOrWhiteSpace(article)) return fallback;

        logger.LogInformation("Kaynak makale metni alındı ({Len} kr): {Url}", article!.Length, item.SourceUrl);
        return $"Baslik: {item.RawTitle}\n\n" +
               "KAYNAK MAKALENIN TAM METNI asagida. Bu metni OZGUN sekilde, kendi cumlelerinle ve KENDI KURGUNLA YENIDEN YAZ; " +
               "cumleleri birebir KOPYALAMA/cevirme, paragraf sirasini izleme. Kaynaktaki TUM somut olgulari " +
               "(sayilar, tarihler, adetler, kisi/kurum adlari, kararlar, takvimler) ve atiflari KORU; olmayani uydurma; " +
               "kaynak hakkinda meta cumle ('kaynak belirtmiyor' gibi) yazma.\n\n" +
               $"--- KAYNAK MAKALE ---\n{article}";
    }

    // ---- Elle (panel) AI üretimi ----

    /// <summary>Panelden bir içeriğin TÜM alanlarını AI ile üretir (yeni revizyon). Görsel/yayın yapmaz — önizleme/düzenleme için.</summary>
    public async Task<Result> GenerateDraftAsync(Guid contentItemId, string? seedInput, CancellationToken ct)
    {
        if (await killSwitch.IsAiStoppedAsync(null, ct))
            return Result.Failure(Error.Conflict("AI üretimi durdurulmuş (kill-switch)."));

        var item = await repository.GetAsync(contentItemId, ct);
        if (item is null) return Result.Failure(Error.NotFound("İçerik"));

        // Girdi kuralı: KISA seed (panel kutusu RSS özetiyle dolu gelir) TAM MAKALEYİ GÖLGELEMESİN.
        // Eskiden kutu doluysa model YALNIZ 1-2 cümlelik özeti görüyordu → olguları bilmeden aynı
        // fikri tekrarlayıp şişiriyordu (Gurman atfı, gizlilik, pilot gibi ayrıntılar hiç gelemezdi).
        // Şimdi: uzun seed (kullanıcının yapıştırdığı gerçek metin) tek başına esas alınır; kısa seed
        // ise kaynak sayfadan çıkarılan TAM makale metniyle BİRLEŞTİRİLİR (seed ek not olarak başta).
        string input;
        var seed = seedInput?.Trim();
        if (string.IsNullOrWhiteSpace(seed))
            input = await BuildAiInputAsync(item, ct);
        else if (seed!.Length >= 1200 || string.IsNullOrWhiteSpace(item.SourceUrl))
            input = seed;
        else
            input = "EK NOT/OZET (panelden): " + seed + "\n\n" + await BuildAiInputAsync(item, ct);
        try
        {
            // Panelden üretim: KISMİ sonuç da kabul (hiç doldurmamaktan iyidir; eksik alan ✨ ile tamamlanır).
            var f = await RunTextAsync(input, item.RawTitle, allowPartial: true, ct);
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
            return Result.Failure(Error.Conflict("AI üretimi başarısız: " + FriendlyAiError(ex)));
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
            var result = await GenerateWithNetRetryAsync(new TextGenerationRequest(system, context, "tr", "v1"), ct);
            return Result.Success(CleanPlain(result.RawJson));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Alan üretimi başarısız: {Field}", req.Field);
            return Result.Failure<string>(Error.Conflict("AI üretimi başarısız: " + FriendlyAiError(ex)));
        }
    }

    private async Task<Fields> RunTextAsync(string input, string? fallbackTitle, bool allowPartial, CancellationToken ct)
    {
        // 1. deneme (anlık ağ/DNS kopmasında 2 sn arayla bir kez daha denenir)
        var result = await GenerateWithNetRetryAsync(new TextGenerationRequest(SystemPrompt, input, "tr", "v1"), ct);
        var first = ParseFields(result.RawJson, fallbackTitle ?? "Baslik");
        if (IsUsable(first)) return first;

        // Kritik alanlar boş/eksik → TEK otomatik düzeltme denemesi (kullanıcı 4-5 kez basmasın).
        logger.LogWarning("AI çıktısı eksik/parse edilemedi, otomatik yeniden deneniyor. İlk 200: {Raw}",
            result.RawJson.Length > 200 ? result.RawJson[..200] : result.RawJson);
        var strict = SystemPrompt +
            " SON UYARI: Cikti SADECE tek bir GECERLI JSON NESNESI olacak — kod citi (```), aciklama, giris cumlesi, JSON disinda TEK KARAKTER bile YOK. " +
            "TUM alanlar DOLU olacak: title, shortX, bodyHtml (kaynak yeterliyse 600+ kelime; kaynak azsa TEKRARLA SISIRMEDEN dogal uzunluk), instagramCaption, tags, primaryKeyword, imageAltText.";
        Fields second;
        string retryRaw;
        try
        {
            var retry = await GenerateWithNetRetryAsync(new TextGenerationRequest(strict, input, "tr", "v1"), ct);
            retryRaw = retry.RawJson;
            second = ParseFields(retryRaw, fallbackTitle ?? "Baslik");
        }
        catch (Exception ex) when (allowPartial && HasAnyContent(first))
        {
            // Düzeltme denemesi ağda/serviste düştü ama ilk denemeden işe yarar alanlar var → onları kullan.
            logger.LogWarning(ex, "Düzeltme denemesi başarısız — ilk denemenin KISMİ sonucu kullanılıyor.");
            return first;
        }
        if (IsUsable(second)) return second;

        // İki denemenin EN İYİ alanlarını birleştir. Panelden üretimde KISMİ sonuç da doldurulur —
        // hiç doldurmamaktan iyidir (eski davranış); eksik alanlar boş kalır, ✨ tek-alan üretimiyle tamamlanır.
        var merged = MergeBest(first, second);
        if (allowPartial && HasAnyContent(merged))
        {
            logger.LogWarning("AI çıktısı kısmi — eldeki alanlar panele dolduruldu (bazıları eksik olabilir).");
            return merged;
        }

        // Hâlâ olmadı → SESSİZCE boş dönme, açık hata ver (panelde sebep görünsün).
        var peek = retryRaw.Trim();
        throw new InvalidOperationException(
            "AI beklenen formatta içerik dönmedi (title/shortX/bodyHtml eksik). Model çıktısının başı: " +
            (peek.Length > 220 ? peek[..220] : peek));
    }

    /// <summary>Anlık ağ/DNS kopmasında ("No such host", zaman aşımı) 2 sn bekleyip BİR kez daha dener.
    /// DNS bazen tek istekte çözülemiyor; ikinci deneme çoğu anlık kopmayı kurtarır. 401 gibi API
    /// hataları HttpRequestException DEĞİLDİR — onlarda tekrar denenmez (aynı hatayı verir).</summary>
    private async Task<TextGenerationResult> GenerateWithNetRetryAsync(TextGenerationRequest req, CancellationToken ct)
    {
        try { return await textProvider.GenerateAsync(req, ct); }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "OpenAI ağ hatası — 2 sn sonra bir kez daha denenecek.");
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
            return await textProvider.GenerateAsync(req, ct);
        }
    }

    /// <summary>İki denemenin alan alan EN DOLU olanını seçer (1. deneme body, 2. deneme shortX getirmiş olabilir).</summary>
    private static Fields MergeBest(Fields a, Fields b) => new(
        Longer(a.Title, b.Title), Longer(a.ShortX, b.ShortX), Longer(a.BodyHtml, b.BodyHtml),
        (b.InstagramCaption?.Length ?? 0) > (a.InstagramCaption?.Length ?? 0) ? b.InstagramCaption : a.InstagramCaption,
        b.Tags.Count > a.Tags.Count ? b.Tags : a.Tags,
        a.PrimaryKeyword ?? b.PrimaryKeyword,
        a.ImageAltText ?? b.ImageAltText);

    private static string Longer(string x, string y) => (y?.Length ?? 0) > (x?.Length ?? 0) ? y : x;

    /// <summary>Panele doldurmaya değer HERHANGİ bir alan var mı? (Title sayılmaz — fallback'ten hep dolu.)</summary>
    private static bool HasAnyContent(Fields f) =>
        !string.IsNullOrWhiteSpace(f.ShortX) || (f.BodyHtml?.Length ?? 0) >= 50 ||
        !string.IsNullOrWhiteSpace(f.InstagramCaption) || f.Tags.Count > 0;

    /// <summary>Teknik hata metnini panelde anlaşılır hale getirir (ne yapılacağı yazsın).</summary>
    private static string FriendlyAiError(Exception ex)
    {
        var m = ex.Message;
        if (ex is HttpRequestException && (m.Contains("No such host", StringComparison.OrdinalIgnoreCase)
            || m.Contains("connection attempt", StringComparison.OrdinalIgnoreCase)
            || m.Contains("timed out", StringComparison.OrdinalIgnoreCase)))
            return "Sunucu api.openai.com'a ULAŞAMIYOR (DNS/ağ sorunu — kod değil). Sunucunun DNS ayarına 8.8.8.8 / 1.1.1.1 ekleyin, " +
                   "'ipconfig /flushdns' çalıştırın ve güvenlik duvarının 443 çıkışına izin verdiğini kontrol edin. Teknik: " + m;
        if (m.Contains("insufficient permissions", StringComparison.OrdinalIgnoreCase))
            return m + " → API anahtarınız bu MODELE erişemiyor. Ayarlar'daki model adını erişiminiz olan bir modelle değiştirin " +
                   "ya da platform.openai.com'da anahtarın izinlerini (Restricted → All / model erişimi) genişletin.";
        return m;
    }

    /// <summary>Kritik alanlar dolu mu? (başlık + X metni + en az ~150 karakter gövde)</summary>
    private static bool IsUsable(Fields f) =>
        !string.IsNullOrWhiteSpace(f.Title) && !string.IsNullOrWhiteSpace(f.ShortX) &&
        (f.BodyHtml?.Length ?? 0) >= 150;

    private static string? FieldPrompt(string field) => field switch
    {
        "title" => "Aşağıdaki içerikten TÜRKÇE, özgün, SOMUT, SEO uyumlu, tıklanır YENİ bir başlık üret. Kaynağın başlığını aynen kullanma/birebir çevirme AMA kaynaktaki asıl konuyu yansıt (gerekiyorsa kişi/kurum adı geçebilir); genel/klişe olmasın; ana anahtar kelimeyi içersin, ~50-65 karakter. Kaynak İngilizce olsa bile çıktı Türkçe olsun. YALNIZCA başlığı düz metin döndür; tırnak/açıklama/JSON yok.",
        "shortX" => "Aşağıdaki içerikten X (Twitter) için TÜRKÇE, EN FAZLA 280 karakter, TEK BAŞINA anlaşılır NET bir ÖZET üret: kim + ne dedi/ne oldu açık olsun (kişi/kurum + iddia). Kaynakta bir kişi/kuruma atıf varsa koru, 'uzmanlar' gibi belirsizleştirme; sitenin kendi görüşü gibi yazma. Belirsiz teaser değil (gerekirse 1-2 hashtag). YALNIZCA metni döndür.",
        "bodyHtml" => "Aşağıdaki kaynağı TÜRKÇE, özgün, telifsiz ve SEO'ya uygun bir blog gövdesi olarak AKTAR (hedef 600-1000 kelime; ama kaynak azsa AYNI fikirleri tekrarlayarak şişirme — dolu 400 kelime, tekrarlı 800 kelimeden iyidir). Kaynaktaki TÜM somut olguları (sayı, tarih, adet, kişi/kurum, karar, takvim) metne taşı; kaynak birden çok neden/katman sayıyorsa hepsini tek tek yaz. 'Kaynak belirtmiyor' gibi meta cümleler YASAK — bilgi yoksa o konuyu hiç açma. Kaynağın paragraf sırasını izleme; olguları önem sırasına göre kendi kurgunla düzenle. Kaynaktaki kişi/kurum atıflarını koru ('uzmanlar' gibi belirsizleştirme, sitenin görüşü gibi sunma); uydurma bilgi ekleme. En az 2-3 <h2> (gerekirse <h3>) alt başlık kullan; her paragrafı <p>...</p> ile sar; ana anahtar kelimeyi başlıkta ve ilk paragrafta doğal kullan. Kaynak İngilizce olsa bile çıktı Türkçe olsun. YALNIZCA HTML gövdeyi döndür.",
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

    /// <summary>Model çıktısından JSON gövdesini söker: kod çiti (```), baştaki/sondaki açıklama lafları
    /// temizlenir; olmadı ilk '{' ile son '}' arası denenir. json_object modu buna rağmen bazen deleniyor.</summary>
    internal static string ExtractJson(string raw)
    {
        var s = (raw ?? "").Trim();
        if (s.StartsWith("```"))
        {
            var nl = s.IndexOf('\n');
            if (nl >= 0) s = s[(nl + 1)..];
            var fence = s.LastIndexOf("```", StringComparison.Ordinal);
            if (fence >= 0) s = s[..fence];
            s = s.Trim();
        }
        if (s.StartsWith('{') && s.EndsWith('}')) return s;
        var i = s.IndexOf('{');
        var j = s.LastIndexOf('}');
        return i >= 0 && j > i ? s[i..(j + 1)] : s;
    }

    internal static Fields ParseFields(string json, string fallbackTitle)
    {
        try
        {
            using var doc = JsonDocument.Parse(ExtractJson(json));
            // Alan adları TOLERANSLI: büyük/küçük harf + snake_case/kebab-case ("body_html" = "bodyHtml").
            var map = NormalizedMap(doc.RootElement);
            // Alanlar kökte değil TEK bir iç nesnede olabilir ({"data":{...}}, {"article":{...}}) → içeri gir.
            if (!map.ContainsKey("title") && !map.ContainsKey("bodyhtml"))
                foreach (var v in map.Values)
                    if (v.ValueKind == JsonValueKind.Object)
                    {
                        var inner = NormalizedMap(v);
                        if (inner.ContainsKey("title") || inner.ContainsKey("bodyhtml")) { map = inner; break; }
                    }

            // İlk DOLU string'i döndür (eş anlamlı alan adları sırayla denenir).
            string S(params string[] keys)
            {
                foreach (var k in keys)
                    if (map.TryGetValue(k, out var v) && v.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(v.GetString()))
                        return v.GetString()!;
                return "";
            }

            // tags: dizi YA DA virgüllü string kabul edilir
            var tags = new List<string>();
            if (map.TryGetValue("tags", out var t) || map.TryGetValue("etiketler", out t) || map.TryGetValue("keywords", out t))
            {
                if (t.ValueKind == JsonValueKind.Array)
                    tags = t.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList();
                else if (t.ValueKind == JsonValueKind.String)
                    tags = t.GetString()!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            }

            var title = S("title", "baslik", "başlık", "headline");
            return new Fields(
                title is { Length: > 0 } ? title : fallbackTitle,
                S("shortx", "xtext", "tweet", "xpost", "shorttext"),
                S("bodyhtml", "body", "html", "content", "article", "bodytext"),
                S("instagramcaption", "instagram", "igcaption", "caption") is { Length: > 0 } ig ? ig : null,
                tags,
                S("primarykeyword", "keyword", "anahtarkelime") is { Length: > 0 } pk ? pk : null,
                S("imagealttext", "imagealt", "alttext", "alt") is { Length: > 0 } alt2 ? alt2 : null);
        }
        catch { return new Fields(fallbackTitle, "", "", null, new List<string>(), null, null); }
    }

    /// <summary>Nesne özelliklerini normalize anahtarla (alt çizgi/tire atılır; harf duyarsız) haritalar.</summary>
    private static Dictionary<string, JsonElement> NormalizedMap(JsonElement r)
    {
        var map = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (r.ValueKind == JsonValueKind.Object)
            foreach (var p in r.EnumerateObject())
                map[p.Name.Replace("_", "").Replace("-", "")] = p.Value;
        return map;
    }

    internal sealed record Fields(
        string Title, string ShortX, string BodyHtml, string? InstagramCaption,
        IReadOnlyList<string> Tags, string? PrimaryKeyword, string? ImageAltText);
}
