using System.Text.Json;
using ContentPlatform.Platform.Domain;
using ContentPlatform.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContentPlatform.Platform.Application;

/// <summary>Hesap oluşturma/hedef ekleme — kimlik bilgisi burada şifrelenir (arayüzden yönetim).</summary>
public sealed class SocialAccountService(
    ISocialAccountRepository repository,
    ICredentialProtector protector,
    IClock clock,
    ILogger<SocialAccountService> logger)
{
    public async Task<Result<Guid>> CreateAsync(CreateSocialAccountRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.DisplayName))
            return Result.Failure<Guid>(Error.Validation("Görünen ad gerekli."));
        if (req.Credentials.Count == 0)
            return Result.Failure<Guid>(Error.Validation("Kimlik bilgileri boş olamaz."));
        if (ValidateCredentials(req.Platform, req.Credentials) is { } err)
            return Result.Failure<Guid>(err);

        var json = JsonSerializer.Serialize(req.Credentials);
        var encrypted = protector.Protect(json);

        var account = new SocialAccount(req.Platform, req.DisplayName, encrypted, req.TokenExpiresAt, req.SiteId, clock);
        await repository.AddAsync(account, ct);
        await repository.SaveChangesAsync(ct);
        return account.Id;
    }

    /// <summary>
    /// Hesabı panelden düzenle: görünen ad + (dolu gönderilen) kimlik alanları. Kimlikte YALNIZ dolu
    /// anahtarlar mevcut değerlerin ÜZERİNE yazılır — boş bırakılan alanlar korunur, token'ı yeniden
    /// girmek gerekmez. Yanlış girilmiş BotToken'ı ("159753xX?*" vakası) silmeden düzeltmeyi sağlar.
    /// </summary>
    public async Task<Result> UpdateAccountAsync(Guid id, UpdateSocialAccountRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.DisplayName))
            return Result.Failure(Error.Validation("Görünen ad gerekli."));

        var account = await repository.GetAsync(id, ct);
        if (account is null) return Result.Failure(Error.NotFound("Hesap"));

        string? encrypted = null;
        var incoming = (req.Credentials ?? new()).Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                                                 .ToDictionary(kv => kv.Key, kv => kv.Value.Trim());
        if (incoming.Count > 0)
        {
            Dictionary<string, string> merged;
            try { merged = DecryptCredentials(account); }
            catch { merged = new(); } // çözülemeyen eski kayıt → yeni girilenlerle baştan kur
            foreach (var (k, v) in incoming) merged[k] = v;

            if (ValidateCredentials(account.Platform, merged) is { } err)
                return Result.Failure(err);

            encrypted = protector.Protect(JsonSerializer.Serialize(merged));
        }

        account.UpdateInfo(req.DisplayName.Trim(), encrypted, req.TokenExpiresAt, clock);
        try
        {
            await repository.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Hesap güncellenemedi (DB): {Id}", id);
            return Result.Failure(Error.Conflict("Hesap güncellenemedi: " + (ex.InnerException?.Message ?? ex.Message)));
        }
        return Result.Success();
    }

    /// <summary>Platforma özgü kimlik biçim denetimi (şimdilik Telegram): hata varsa döner, yoksa null.</summary>
    private static Error? ValidateCredentials(ContentPlatform.Abstractions.Platform platform, Dictionary<string, string> creds)
    {
        if (platform == ContentPlatform.Abstractions.Platform.Telegram
            && creds.TryGetValue("BotToken", out var bt)
            && !ContentPlatform.Abstractions.TelegramToken.LooksValid(bt))
        {
            return Error.Validation("Telegram BotToken biçimi geçersiz. BotFather'ın verdiği '123456789:AA…' biçimindeki token'ı girin (şifre değil).");
        }
        return null;
    }

    public async Task<Result> AddTargetAsync(Guid accountId, AddTargetRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ExternalTargetId))
            return Result.Failure(Error.Validation("Dış ID (chat/kanal id) gerekli."));
        if (string.IsNullOrWhiteSpace(req.Title))
            return Result.Failure(Error.Validation("Başlık gerekli."));

        var account = await repository.GetAsync(accountId, ct);
        if (account is null) return Result.Failure(Error.NotFound("Hesap"));

        var extId = req.ExternalTargetId.Trim();
        if (await repository.TargetExistsAsync(accountId, extId, null, ct))
            return Result.Failure(Error.Conflict($"Bu Dış ID zaten bu hesapta tanımlı: {extId}. Mevcut hedefi düzenleyin veya farklı bir ID girin."));

        account.AddTarget(extId, req.Type, req.Role, req.CategoryId, req.Title.Trim(), req.Language, req.TimeZone, req.CharacterLimit, clock,
            req.ShowOnHome, NormalizeUrl(req.PublicUrl), req.FollowerCount);
        try
        {
            await repository.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Hedef eklenemedi (DB): hesap={Account} extId={Ext}", accountId, extId);
            return Result.Failure(Error.Conflict("Hedef kaydedilemedi: " + (ex.InnerException?.Message ?? ex.Message)));
        }
        return Result.Success();
    }

    public async Task<Result> UpdateTargetAsync(Guid targetId, UpdateTargetRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ExternalTargetId))
            return Result.Failure(Error.Validation("Dış ID (chat/kanal id) gerekli."));
        if (string.IsNullOrWhiteSpace(req.Title))
            return Result.Failure(Error.Validation("Başlık gerekli."));

        var target = await repository.GetTargetAsync(targetId, ct);
        if (target is null) return Result.Failure(Error.NotFound("Hedef"));

        var extId = req.ExternalTargetId.Trim();
        if (await repository.TargetExistsAsync(target.SocialAccountId, extId, targetId, ct))
            return Result.Failure(Error.Conflict($"Bu Dış ID zaten bu hesapta başka bir hedefte tanımlı: {extId}."));

        target.Update(extId, req.Type, req.Role, req.CategoryId, req.Title.Trim(), req.Language, req.TimeZone, req.CharacterLimit, clock,
            req.ShowOnHome, NormalizeUrl(req.PublicUrl), req.FollowerCount);
        if (req.IsActive) target.Enable(clock); else target.Disable(clock);

        try
        {
            await repository.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Hedef güncellenemedi (DB): hedef={Target}", targetId);
            return Result.Failure(Error.Conflict("Hedef güncellenemedi: " + (ex.InnerException?.Message ?? ex.Message)));
        }
        return Result.Success();
    }

    /// <summary>Adaptörlerin kullanması için kimlik alanlarını çözer (yalnız yayın anında, in-memory).</summary>
    public Dictionary<string, string> DecryptCredentials(SocialAccount account)
    {
        var json = protector.Unprotect(account.CredentialsEncrypted);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
    }

    public async Task<IReadOnlyList<SocialAccountDto>> ListAsync(CancellationToken ct)
    {
        var accounts = await repository.ListAsync(ct);
        return accounts.Select(a => new SocialAccountDto(
            a.Id, a.Platform, a.DisplayName, a.Status, a.TokenExpiresAt, a.LastError, a.Targets.Count)).ToList();
    }

    public async Task<SocialAccountDetailDto?> GetDetailAsync(Guid id, CancellationToken ct)
    {
        var a = await repository.GetAsync(id, ct);
        if (a is null) return null;
        return new SocialAccountDetailDto(a.Id, a.Platform, a.DisplayName, a.Status, a.TokenExpiresAt, a.LastError,
            a.Targets.Select(t => new TargetDto(t.Id, t.Platform, t.ExternalTargetId, t.Type, t.Role, t.CategoryId, t.Title, t.IsActive,
                t.ShowOnHome, t.PublicUrl, t.FollowerCount)).ToList());
    }

    /// <summary>Ana sayfa takip linkini normalize eder: boşsa null; şema yoksa https:// ekler.</summary>
    private static string? NormalizeUrl(string? url)
    {
        url = url?.Trim();
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url;
        return url;
    }

    public async Task<Result> DisableAccountAsync(Guid id, CancellationToken ct)
    {
        var a = await repository.GetAsync(id, ct);
        if (a is null) return Result.Failure(Error.NotFound("Hesap"));
        a.Disable(clock);
        await repository.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Hesabı ve tüm hedeflerini kalıcı olarak siler.</summary>
    public async Task<Result> DeleteAccountAsync(Guid id, CancellationToken ct)
    {
        var a = await repository.GetAsync(id, ct);
        if (a is null) return Result.Failure(Error.NotFound("Hesap"));
        repository.RemoveAccount(a);
        try
        {
            await repository.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Hesap silinemedi (DB): {Id}", id);
            return Result.Failure(Error.Conflict("Hesap silinemedi: " + (ex.InnerException?.Message ?? ex.Message)));
        }
        return Result.Success();
    }

    public async Task<Result> DisableTargetAsync(Guid targetId, CancellationToken ct)
    {
        var t = await repository.GetTargetAsync(targetId, ct);
        if (t is null) return Result.Failure(Error.NotFound("Hedef"));
        t.Disable(clock);
        await repository.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> EnableTargetAsync(Guid targetId, CancellationToken ct)
    {
        var t = await repository.GetTargetAsync(targetId, ct);
        if (t is null) return Result.Failure(Error.NotFound("Hedef"));
        t.Enable(clock);
        await repository.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Hedefi kalıcı olarak siler.</summary>
    public async Task<Result> DeleteTargetAsync(Guid targetId, CancellationToken ct)
    {
        var t = await repository.GetTargetAsync(targetId, ct);
        if (t is null) return Result.Failure(Error.NotFound("Hedef"));
        repository.RemoveTarget(t);
        try
        {
            await repository.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Hedef silinemedi (DB): {Id}", targetId);
            return Result.Failure(Error.Conflict("Hedef silinemedi: " + (ex.InnerException?.Message ?? ex.Message)));
        }
        return Result.Success();
    }
}
