namespace ContentPlatform.Editorial.Domain;

/// <summary>İçeriğin nereden geldiği.</summary>
public enum ContentOrigin { Rss, WebPage, Manual, ManualNoAi, TelegramAdmin }

/// <summary>Görsel kaynağı. Manual = "Ben yükleyeceğim" (metin AI, görsel elle; yüklenene kadar sıraya girmez).</summary>
public enum ImageSource { Ai, SkiaCard, Manual }

/// <summary>İçerik risk seviyesi. Yüksek risk otomatik onaylanmaz.</summary>
public enum RiskLevel { Low, Medium, High }

/// <summary>Editoryal yaşam döngüsü (yayın/medya durumundan AYRI).</summary>
public enum EditorialStatus { Discovered, PendingReview, Draft, Approved, Published, Archived, Rejected }

/// <summary>Görsel hazırlık durumu. AwaitingManualUpload = elle yükleme bekliyor, içerik sıraya girmez.</summary>
public enum MediaStatus { NotRequired, Pending, AwaitingManualUpload, Ready, Failed }

public enum ActorType { AdminUser, TelegramMember, System }
