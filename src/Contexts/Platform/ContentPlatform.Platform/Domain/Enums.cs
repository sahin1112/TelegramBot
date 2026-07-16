namespace ContentPlatform.Platform.Domain;

public enum AccountStatus { Active, Error, Disabled }

/// <summary>Fiziksel yayın hedefi türü. Bir bot/hesap birden çok hedefe yayabilir.</summary>
public enum TargetType { Group, Channel, Profile, Feed }

/// <summary>Hedef rolü. AdminInbox editoryal içerik almaz (yalnız girdi dinler).</summary>
public enum TargetRole { Editorial, Test, AdminInbox }
