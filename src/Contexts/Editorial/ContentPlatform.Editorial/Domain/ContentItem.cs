using ContentPlatform.SharedKernel;

namespace ContentPlatform.Editorial.Domain;

/// <summary>
/// Pipeline'ın kalbi. Editoryal ve medya durumları AYRI eksenlerde tutulur;
/// tek bir kanal patlasa bile içerik "başarısız" olmaz (o durum Publication seviyesinde).
/// </summary>
public sealed class ContentItem : Entity
{
    private readonly List<ContentRevision> _revisions = new();
    private readonly List<MediaAsset> _media = new();

    private ContentItem() { } // EF

    public ContentItem(
        ContentOrigin origin,
        bool useAi,
        ImageSource imageSource,
        RiskLevel riskLevel,
        Guid? categoryId,
        bool testMode,
        string sourceHash,
        string? sourceUrl,
        string? rawTitle,
        string? rawInput,
        ActorType createdByType,
        string createdByRef,
        IClock clock)
    {
        Origin = origin;
        UseAi = useAi;
        ImageSource = imageSource;
        RiskLevel = riskLevel;
        CategoryId = categoryId;
        TestMode = testMode;
        SourceHash = sourceHash;
        SourceUrl = sourceUrl;
        RawTitle = rawTitle;
        RawInput = rawInput;
        CreatedByType = createdByType;
        // Kolon sınırına (1000) savunmacı kırpma: aşırı uzun kaynak URL'leri INSERT'i patlatmasın
        // ("String or binary data would be truncated" — content_items.CreatedByRef hatası düzeltmesi).
        CreatedByRef = createdByRef.Length <= 1000 ? createdByRef : createdByRef[..1000];
        CreatedAt = clock.UtcNow;

        // Girdi tipine göre başlangıç durumu (bkz. 00 §7).
        EditorialStatus = origin switch
        {
            // RSS/sayfa → önce TASLAK (ham). İçerik üretilip "Onaya gönder" ile onay kuyruğuna (PendingReview) taşınır.
            ContentOrigin.Rss or ContentOrigin.WebPage => EditorialStatus.Draft,
            ContentOrigin.Manual or ContentOrigin.ManualNoAi => EditorialStatus.Approved, // güvenilir → otomatik onaylı
            _ => EditorialStatus.Draft // TelegramAdmin -> taslak (ReviewDraft)
        };
        MediaStatus = MediaStatus.Pending;
    }

    public ContentOrigin Origin { get; private set; }
    public bool UseAi { get; private set; }
    public ImageSource ImageSource { get; private set; }
    public RiskLevel RiskLevel { get; private set; }
    public Guid? CategoryId { get; private set; }
    public bool TestMode { get; private set; }
    public string SourceHash { get; private set; } = default!;
    public string? SourceUrl { get; private set; }
    public string? RawTitle { get; private set; }
    public string? RawInput { get; private set; }

    public EditorialStatus EditorialStatus { get; private set; }
    public MediaStatus MediaStatus { get; private set; }

    public ActorType CreatedByType { get; private set; }
    public string CreatedByRef { get; private set; } = default!;
    public string? ApprovedByRef { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public DateTimeOffset? ScheduledAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public string? Error { get; private set; }

    public IReadOnlyList<ContentRevision> Revisions => _revisions;
    public IReadOnlyList<MediaAsset> Media => _media;

    // ---- Durum geçişleri (kurallar burada; kör setter yok) ----

    /// <summary>
    /// Onaylar. <paramref name="automated"/>=true (otomatik/RssAutoApprove) ise yüksek riskli içerik
    /// REDDEDİLİR — her zaman insan onayı gerekir (00 §10). İnsan onayı (automated=false) risk ne olursa olsun geçer.
    /// </summary>
    public Result Approve(string approverRef, bool automated, IClock clock)
    {
        if (RiskLevel == RiskLevel.High && automated)
            return Result.Failure(ContentPlatform.SharedKernel.Error.Validation("Yüksek riskli içerik otomatik onaylanamaz; insan onayı gerekir."));
        if (EditorialStatus is not (EditorialStatus.PendingReview or EditorialStatus.Draft))
            return Result.Failure(ContentPlatform.SharedKernel.Error.Conflict($"Onay için uygun durumda değil: {EditorialStatus}."));

        ApprovedByRef = approverRef;
        ApprovedAt = clock.UtcNow;
        EditorialStatus = EditorialStatus.Approved;
        Error = null; // varsa önceki inceleme/kalite notunu temizle
        Touch(clock);
        return Result.Success();
    }

    /// <summary>
    /// Kalite kapısı kritik sorun bulunca içeriği İNSAN incelemesine geri alır (00 §27-B):
    /// oto-yeniden-üretime değil, Draft'a düşer; sebep saklanır. Üretim sorgusu (Approved+Pending) bunu tekrar almaz.
    /// </summary>
    public void HoldForReview(string reason, IClock clock)
    {
        EditorialStatus = EditorialStatus.Draft;
        Error = reason;
        Touch(clock);
    }

    public Result Reject(string actorRef, IClock clock)
    {
        if (EditorialStatus is not (EditorialStatus.PendingReview or EditorialStatus.Draft))
            return Result.Failure(ContentPlatform.SharedKernel.Error.Conflict("Reddedilecek durumda değil."));
        EditorialStatus = EditorialStatus.Rejected;
        Touch(clock);
        return Result.Success();
    }

    /// <summary>Onay öncesi görsel kaynağını değiştirir (AI / SkiaCard / Ben yükleyeceğim).</summary>
    public void SetTestMode(bool testMode, IClock clock)
    {
        TestMode = testMode;
        Touch(clock);
    }

    /// <summary>İçeriği onay kuyruğuna (PendingReview) al — otomatik yayınlanmaz; önce üret/gözden geçir.</summary>
    public void ReturnToReview(IClock clock)
    {
        EditorialStatus = EditorialStatus.PendingReview;
        Touch(clock);
    }

    /// <summary>
    /// Taslağı ONAYA gönderir (Draft → PendingReview). Yalnız güncel bir revizyon (üretilmiş/düzenlenmiş içerik)
    /// varsa geçer — ham/boş taslak onaya gönderilemez.
    /// </summary>
    public Result SubmitForReview(IClock clock)
    {
        if (EditorialStatus is not EditorialStatus.Draft)
            return Result.Failure(ContentPlatform.SharedKernel.Error.Conflict($"Onaya gönderilecek durumda değil: {EditorialStatus}."));
        if (!_revisions.Any(r => r.IsCurrent))
            return Result.Failure(ContentPlatform.SharedKernel.Error.Validation("Önce içeriği üretin/düzenleyin (güncel revizyon yok)."));
        EditorialStatus = EditorialStatus.PendingReview;
        Error = null;
        Touch(clock);
        return Result.Success();
    }

    /// <summary>Elle yayın zamanı belirle (null → kategori politikası / hemen).</summary>
    public void Schedule(DateTimeOffset? at, IClock clock)
    {
        ScheduledAt = at;
        Touch(clock);
    }

    public void SetImageSource(ImageSource source, IClock clock)
    {
        ImageSource = source;
        Touch(clock);
    }

    /// <summary>Yayından geri çek / arşivle (daha fazla işlenmez).</summary>
    public Result Archive(IClock clock)
    {
        EditorialStatus = EditorialStatus.Archived;
        Touch(clock);
        return Result.Success();
    }

    /// <summary>"Ben yükleyeceğim" akışı: metin hazır, görsel elle beklenir; içerik sıraya girmez.</summary>
    public void MarkAwaitingManualImage(IClock clock)
    {
        MediaStatus = MediaStatus.AwaitingManualUpload;
        Touch(clock);
    }

    public void MarkMediaReady(IClock clock)
    {
        MediaStatus = MediaStatus.Ready;
        Touch(clock);
    }

    public void MarkPublished(IClock clock)
    {
        EditorialStatus = EditorialStatus.Published;
        PublishedAt = clock.UtcNow;
        Touch(clock);
    }

    public ContentRevision AddRevision(ContentRevision revision)
    {
        foreach (var r in _revisions) r.Supersede();
        _revisions.Add(revision);
        return revision;
    }

    public MediaAsset AddMedia(MediaKind kind, string url, int width, int height, bool titleBurned, IClock clock)
    {
        var m = new MediaAsset(Id, kind, url, width, height, titleBurned, clock);
        _media.Add(m);
        Touch(clock);
        return m;
    }
}
