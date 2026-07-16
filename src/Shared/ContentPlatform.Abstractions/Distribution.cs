namespace ContentPlatform.Abstractions;

/// <summary>Hedef rolü. AdminInbox editoryal içerik ALMAZ (yalnız girdi dinler).</summary>
public enum TargetRole { Editorial, Test, AdminInbox }

/// <summary>Çözülmüş bir yayın hedefi (hangi hesap + dış hedef id).</summary>
public sealed record ResolvedTarget(Guid SocialAccountId, string ExternalTargetId, Channel Channel);

/// <summary>
/// Bir içerik için doğru hedefleri çözer. Kural: testMode ise Test rolü, değilse Editorial rolü;
/// AdminInbox HER ZAMAN hariç. İsteğe bağlı kategori kapsamı.
/// </summary>
public interface IPublicationTargetResolver
{
    Task<IReadOnlyList<ResolvedTarget>> ResolveAsync(Guid? categoryId, bool testMode, Channel channel, CancellationToken ct);
}

/// <summary>Bir hesabın çözülmüş (decrypt) kimlik bilgilerini verir (yalnız yayın anında).</summary>
public interface IAccountCredentialProvider
{
    Task<AccountCredentials?> GetAsync(Guid socialAccountId, CancellationToken ct);
}
