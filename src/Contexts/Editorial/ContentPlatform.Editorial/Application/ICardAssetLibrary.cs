namespace ContentPlatform.Editorial.Application;

/// <summary>Kategori görsel şablon kütüphanesi (wwwroot/assets/cards/{1x1,reels}). Panelde listelenir, üretimde okunur.</summary>
public interface ICardAssetLibrary
{
    /// <summary>kind: "1x1" | "reels". Klasördeki görsel dosya adları (sıralı). Klasör yoksa boş.</summary>
    IReadOnlyList<string> List(string kind);
    /// <summary>Bir şablonun baytları; yoksa null. fileName güvenli olmalı (yol ayracı / ".." reddedilir).</summary>
    byte[]? Read(string kind, string fileName);
}
