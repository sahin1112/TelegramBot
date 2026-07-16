# Veritabanı ve Migration

**Motor:** SQL Server 2022 (Docker: `mssql/server:2022-latest`, port `1433`).
**Migration stratejisi:** modül başına ayrı migration; **uygulama (Api) ayağa kalkarken bekleyen migration'lar otomatik uygulanır** (`Database:AutoMigrate=true`). Sunucunun DB'sine dışarıdan erişim gerekmez.

## Bağlantı dizeleri
- **Sunucu (production):** `src/Host/*/appsettings.json` → `ConnectionStrings:Default` (şifreyi sunucuda değiştir).
- **Yerel (Development):** `src/Host/*/appsettings.Development.json` → iki cihaz için iki satır; aktif olanı bırak, diğerini yorumla.

## Sunucu kurulumu (bir kez)
1. `scripts/db/create-database.sql` içindeki `REPLACE_ON_SERVER` şifresini değiştir.
2. Script'i sunucuda çalıştır (DB + `contentplatform` kullanıcısı, db_owner).
3. `appsettings.json` içindeki `Password=REPLACE_ON_SERVER` değerini aynı şifreyle güncelle (ya da ortam değişkeni `ConnectionStrings__Default` ver).
4. Api'yi başlat → migration'lar otomatik uygulanır.

## İlk migration'ları üretme (geliştirici makinesinde, bir kez)
Araç: `dotnet tool install --global dotnet-ef` (veya `dotnet tool update -g dotnet-ef`).

Design-time factory sayesinde her bağlam kendi başına migrate edilebilir:

```bash
dotnet ef migrations add Initial --context EditorialDbContext  -p src/Contexts/Editorial/ContentPlatform.Editorial   -o Infrastructure/Migrations
dotnet ef migrations add Initial --context PlatformDbContext   -p src/Contexts/Platform/ContentPlatform.Platform     -o Infrastructure/Migrations
dotnet ef migrations add Initial --context IngestionDbContext  -p src/Contexts/Ingestion/ContentPlatform.Ingestion   -o Infrastructure/Migrations
dotnet ef migrations add Initial --context PublishingDbContext -p src/Contexts/Publishing/ContentPlatform.Publishing -o Infrastructure/Migrations
```

Üretilen migration'lar commit edilir; sonraki her açılışta otomatik uygulanır.
Design-time bağlantı için `CONTENTPLATFORM_DB` ortam değişkeni kullanılır (yoksa yereldeki SQL Server varsayılır).

> Not: Migration'ları Api uygular. Worker yalnız bağlanır (Api'yi bir kez başlatıp şema oluşturulduktan sonra Worker sorunsuz çalışır).
