using ContentPlatform.Abstractions;
using Microsoft.Extensions.Options;

namespace ContentPlatform.Publishing.Infrastructure.OpenAi;

/// <summary>
/// OpenAI METİN isteklerini SÜREÇ genelinde SIRAYA sokar: aynı anda EN ÇOK N istek (varsayılan 1) ve
/// ardışık istek başlangıçları arasında EN AZ MinInterval kadar bekleme. "Atak gibi" toplu üretimde
/// (RSS otomasyonu 20 haberi birden işleyince) OpenAI'nin istekleri reddetmesini (429/kısa kesinti)
/// önler. SINGLETON'dır — tüm scope'lar/işler AYNI kapıyı paylaşır; bu yüzden alanlar thread-safe kilitli.
/// </summary>
public sealed class AiTextThrottle : IDisposable
{
    private readonly SemaphoreSlim _slots;         // aynı anda kaç istek
    private readonly SemaphoreSlim _spacingLock = new(1, 1); // istek başlangıçlarını tek tek boşluklandırır
    private readonly int _minIntervalMs;
    private long _lastStartTick;                   // Environment.TickCount64 (monoton)
    private readonly int _quotaCooldownMs;         // kota bitince ne kadar durulacak
    private long _quotaResumeTick;                 // 0 = normal; >0 = bu tick'e kadar AI durdur

    public AiTextThrottle(IOptions<OpenAiOptions> opt)
    {
        var o = opt.Value;
        _slots = new SemaphoreSlim(Math.Max(1, o.TextMaxConcurrent), Math.Max(1, o.TextMaxConcurrent));
        _minIntervalMs = Math.Max(0, o.TextMinIntervalMs);
        _quotaCooldownMs = Math.Max(1, o.TextQuotaCooldownSeconds) * 1000;
    }

    /// <summary>Kota tükendiği için metin üretimi şu an duraklatılmış mı? (cooldown süresi dolunca kendiliğinden açılır)</summary>
    public bool QuotaPaused => _quotaResumeTick != 0 && Environment.TickCount64 < _quotaResumeTick;

    /// <summary>OpenAI 'insufficient_quota' döndürünce çağrılır: aynı kalıcı hatayı 20 haber için tekrar
    /// tekrar almamak adına metin üretimini KISA SÜRE durdurur. Süre dolunca bir kez daha denenir.</summary>
    public void TripQuota() => _quotaResumeTick = Environment.TickCount64 + _quotaCooldownMs;

    /// <summary>Verilen işi kuyruğa alır: önce boş slot bekler, sonra ardışık aralığı uygular, sonra çalıştırır.</summary>
    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        // Kota tükendiyse HTTP'ye hiç çıkma — anında net hata ver (deneme hakkı yakılmaz, çağıran atlar).
        if (QuotaPaused)
            throw new AiQuotaExceededException(
                "OpenAI kotası tükendiği için metin üretimi geçici olarak duraklatıldı; kota/bakiye düzelince kendiliğinden devam eder.");
        await _slots.WaitAsync(ct);
        try
        {
            await SpaceAsync(ct);
            return await action(ct);
        }
        finally { _slots.Release(); }
    }

    /// <summary>Bir önceki isteğin BAŞLANGICINDAN bu yana MinInterval geçmediyse kalanı bekler; ilk istek beklemez.</summary>
    private async Task SpaceAsync(CancellationToken ct)
    {
        if (_minIntervalMs == 0) return;
        await _spacingLock.WaitAsync(ct);
        try
        {
            var now = Environment.TickCount64;
            var wait = _minIntervalMs - (int)(now - _lastStartTick);
            if (_lastStartTick != 0 && wait > 0) await Task.Delay(wait, ct);
            _lastStartTick = Environment.TickCount64;
        }
        finally { _spacingLock.Release(); }
    }

    public void Dispose() { _slots.Dispose(); _spacingLock.Dispose(); }
}
