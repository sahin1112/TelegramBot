namespace ContentPlatform.SharedKernel;

/// <summary>Öngörülebilir hata: exception yerine açık sonuç tipiyle taşınır.</summary>
public sealed record Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);
    public static Error NotFound(string what) => new("not_found", $"{what} bulunamadı.");
    public static Error Validation(string message) => new("validation", message);
    public static Error Conflict(string message) => new("conflict", message);
    public static Error Unexpected(string message) => new("unexpected", message);
    public bool IsNone => this == None;
}
