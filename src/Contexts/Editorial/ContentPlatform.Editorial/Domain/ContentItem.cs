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
        CreatedByRef = createdByRef;
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

    // ---- Otomatik üretim niyeti (RSS keşfinde kategori/kaynak ayarından çözülür) ----
    /// <summary>Kaç başarısız deneme sonrası bir adım "üretilemedi" (Failed) sayılır.</summary>
    public const int MaxAutoAttempts = 3;

    /// <summary>Otomatik metin (AI içerik) üretilsin mi?</summary>
    public bool AutoContent { get; private set; }
    /// <summary>Otomatik (rastgele SkiaCard) görsel üretilsin mi?</summary>
    public bool AutoImage { get; private set; }
    /// <summary>Otomatik SkiaCard slayt videosu üretilsin mi?</summary>
    public bool AutoVideo { get; private set; }
    /// <summary>Üretim bitince otomatik onaylanıp yayına gönderilsin mi? (kapalı = taslakta bekler)</summary>
    public bool AutoPublish { get; private set; }

    public GenStepStatus ContentGen { get; private set; }
    public GenStepStatus ImageGen { get; private set; }
    public GenStepStatus VideoGen { get; private set; }
    public int ContentAttempts { get; private set; }
    public int ImageAttempts { get; private set; }
    public int VideoAttempts { get; private set; }

    // ---- Görsel şablon havuzu (item oluşturulurken kaynak/kategoriden çözülür; boş = varsayılan SkiaCard) ----
    /// <summary>1:1 şablon dosya adları (virgüllü). Boşsa üretimde düz SkiaCard kullanılır.</summary>
    public string Card1x1Pool { get; private set; } = "";
    /// <summary>9:16 (reels/hikaye) şablon dosya adları (virgüllü).</summary>
    public string CardReelsPool { get; private set; } = "";
    /// <summary>Risk seviyesine göre otomatik SON DAKİKA rozeti bu içerik için açık mı? (kategori ayarı)</summary>
    public bool BadgeAuto { get; private set; }
    /// <summary>Elle seçilen rozet: null = otomatik (riske göre), "" = rozet YOK (zorla), aksi = zorla o metin ("SON DAKİKA"/"ŞOK").</summary>
    public string? BadgeOverride { get; private set; }

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

    // ---- Otomatik üretim niyeti + adım sonuçları ----

    /// <summary>RSS keşfinde otomatik üretim niyetini belirler (kaynak ?? kategori ayarından çözülmüş).</summary>
    public void ConfigureAutomation(bool content, bool image, bool video, bool publish, IClock clock)
    {
        AutoContent = content;
        AutoImage = image;
        AutoVideo = video;
        AutoPublish = publish;
        Touch(clock);
    }

    /// <summary>Görsel şablon havuzunu ve rozet ayarını uygular (kaynak/kategoriden çözülmüş).</summary>
    public void ConfigureCards(string card1x1Pool, string cardReelsPool, bool badgeAuto, IClock clock)
    {
        Card1x1Pool = card1x1Pool ?? "";
        CardReelsPool = cardReelsPool ?? "";
        BadgeAuto = badgeAuto;
        Touch(clock);
    }

    /// <summary>Panelden elle rozet seçimi (null=otomatik, ""=yok, "SON DAKİKA"/"ŞOK"=zorla).</summary>
    public void SetBadgeOverride(string? badge, IClock clock)
    {
        BadgeOverride = badge;
        Touch(clock);
    }

    public void MarkContentGenerated(IClock clock) { ContentGen = GenStepStatus.Done; Touch(clock); }
    public void MarkImageGenerated(IClock clock) { ImageGen = GenStepStatus.Done; Touch(clock); }
    public void MarkVideoGenerated(IClock clock) { VideoGen = GenStepStatus.Done; Touch(clock); }

    /// <summary>Başarısız denemeyi kaydeder; MaxAutoAttempts'e ulaşınca adımı kalıcı olarak Failed'a çeker.</summary>
    public void RegisterContentFailure(IClock clock) { ContentAttempts++; if (ContentAttempts >= MaxAutoAttempts) ContentGen = GenStepStatus.Failed; Touch(clock); }
    public void RegisterImageFailure(IClock clock) { ImageAttempts++; if (ImageAttempts >= MaxAutoAttempts) ImageGen = GenStepStatus.Failed; Touch(clock); }
    public void RegisterVideoFailure(IClock clock) { VideoAttempts++; if (VideoAttempts >= MaxAutoAttempts) VideoGen = GenStepStatus.Failed; Touch(clock); }

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
