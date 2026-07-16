using System.Text.Json;
using ContentPlatform.Platform.Domain;
using ContentPlatform.SharedKernel;

namespace ContentPlatform.Platform.Application;

/// <summary>Hesap oluşturma/hedef ekleme — kimlik bilgisi burada şifrelenir (arayüzden yönetim).</summary>
public sealed class SocialAccountService(
    ISocialAccountRepository repository,
    ICredentialProtector protector,
    IClock clock)
{
    public async Task<Result<Guid>> CreateAsync(CreateSocialAccountRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.DisplayName))
            return Result.Failure<Guid>(Error.Validation("Görünen ad gerekli."));
        if (req.Credentials.Count == 0)
            return Result.Failure<Guid>(Error.Validation("Kimlik bilgileri boş olamaz."));

        var json = JsonSerializer.Serialize(req.Credentials);
        var encrypted = protector.Protect(json);

        var account = new SocialAccount(req.Platform, req.DisplayName, encrypted, req.TokenExpiresAt, req.SiteId, clock);
        await repository.AddAsync(account, ct);
        await repository.SaveChangesAsync(ct);
        return account.Id;
    }

    public async Task<Result> AddTargetAsync(Guid accountId, AddTargetRequest req, CancellationToken ct)
    {
        var account = await repository.GetAsync(accountId, ct);
        if (account is null) return Result.Failure(Error.NotFound("Hesap"));
        account.AddTarget(req.ExternalTargetId, req.Type, req.Role, req.CategoryId, req.Title, req.Language, req.TimeZone, req.CharacterLimit, clock);
        await repository.SaveChangesAsync(ct);
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
            a.Targets.Select(t => new TargetDto(t.Id, t.Platform, t.ExternalTargetId, t.Type, t.Role, t.CategoryId, t.Title, t.IsActive)).ToList());
    }

    public async Task<Result> DisableAccountAsync(Guid id, CancellationToken ct)
    {
        var a = await repository.GetAsync(id, ct);
        if (a is null) return Result.Failure(Error.NotFound("Hesap"));
        a.Disable(clock);
        await repository.SaveChangesAsync(ct);
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
}
