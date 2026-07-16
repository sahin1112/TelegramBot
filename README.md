# İçerik Otomasyon Platformu — Çözüm (Solution)

Modular Monolith + Clean Architecture. Hedef: **.NET 9 (`net9.0`)**. Tasarım dokümanları: `docs/` (bkz. `docs/00-PROJE-Ana-Dokuman.md`).

## Yapı
```
ContentPlatform.sln
Directory.Build.props        # ortak TFM (net9.0) + derleme ayarları
Directory.Packages.props     # Central Package Management (tüm sürümler burada)
src/
  Host/
    ContentPlatform.Api/       # blog/admin API + modül keşfi + health  (Sdk.Web)
    ContentPlatform.Worker/    # Quartz zamanlayıcı host               (Sdk.Worker)
  Shared/
    ContentPlatform.SharedKernel/   # Result/Error, Entity, IClock, PagedResult
    ContentPlatform.Abstractions/   # IModule, ModuleRegistrar, portlar (IChannelPublisher, AI), integration events
  Contexts/                    # bounded context'ler (her biri bir IModule)
    Ingestion/     ContentPlatform.Ingestion      (iskelet)
    Editorial/     ContentPlatform.Editorial      (ÖRNEK: Domain/Application/Infrastructure/Api dolu)
    Publishing/    ContentPlatform.Publishing     (iskelet)
    Site/          ContentPlatform.Site           (iskelet)
    Platform/      ContentPlatform.Platform       (iskelet)
```

## Mimari desen
- **Modül keşfi:** her bağlam bir `IModule` (`Register` + `MapEndpoints`). `Program.cs` hepsini yansıma ile bulur, DI'a kaydeder, endpoint'lerini eşler.
- **Katmanlama:** Domain (saf) → Application (arayüzler/DTO) → Infrastructure (EF Core/dış servis) → Api (Minimal API).
- **Adaptör deseni:** `IChannelPublisher`, `ITextGenerationProvider`, `IImageGenerationProvider` — çekirdek sağlayıcıyı bilmez.
- **Durum ayrımı:** `EditorialStatus` ≠ `MediaStatus` ≠ (ileride) `PublicationStatus`. Örnek: `ContentItem` durum geçişleri kural içerir (kör setter yok); yüksek riskli içerik otomatik onaylanmaz; "Ben yükleyeceğim" görselinde `AwaitingManualUpload`.

## Derleme
```bash
dotnet restore
dotnet build
dotnet run --project src/Host/ContentPlatform.Api      # /health modülleri listeler
dotnet run --project src/Host/ContentPlatform.Worker   # SourcePollJob 5 dk'da bir (iskelet)
```
> Not: `net9.0` hedeflenir; makinende .NET 9 SDK + **SQL Server 2022** (Docker `mssql/server:2022-latest`, port 1433) gerekir.
> **Migration'lar Api ayağa kalkarken otomatik uygulanır** (`Database:AutoMigrate`). Bağlantı: sunucu `appsettings.json`, yerel (iki cihaz) `appsettings.Development.json`. Ayrıntı: `scripts/db/README.md`.

## Durum
İskelet + Editorial örnek bağlamı hazır. Sıralı geliştirmede: Ingestion (FactPack/RSS), Publishing (DistributionPlan + adaptörler), Site (Blog/SEO), Platform (SocialAccounts/Token, Community, Audit) doldurulacak.
