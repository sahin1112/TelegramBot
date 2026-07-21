<#
================================================================================
 ContentPlatform - Eksik EF Core migration'larini bul ve uret
 EF Core 9 - 5 bounded context - her context'in kendi design-time factory'si var
--------------------------------------------------------------------------------
 NE YAPAR:
   1) 'dotnet ef' araci var mi diye bakar (yoksa kurar).
   2) Her context icin drift kontrolu yapar
      ('has-pending-model-changes' - migration'i yazilmamis her model
       degisikligini yakalar; veritabanina baglanmaz).
   3) Fark varsa o context icin migration URETIR (yoksa atlar).

 NOT: 'migrations add' DB'ye baglanmaz, sadece derlenmis modele bakar.
      Migration'lar uygulamada acilista otomatik uygulanir
      (Database:AutoMigrate=true); uretip deploy etmen yeterli.

 KULLANIM (repo kokunde, D:\projeler\TelegramBot):
   powershell -ExecutionPolicy Bypass -File .\migrations-ekle.ps1 -CheckOnly
   powershell -ExecutionPolicy Bypass -File .\migrations-ekle.ps1 -Name WidenRefsAndSync
   powershell -ExecutionPolicy Bypass -File .\migrations-ekle.ps1 -Name Sync -Update
================================================================================
#>
[CmdletBinding()]
param(
    [string]$Name = "Sync",
    [switch]$CheckOnly,
    [switch]$Update
)

# ONEMLI: 'Stop' KULLANMA. dotnet, NuGet uyarilarini (NU1902 vb.) stderr'e yazar;
# 'Stop' bunlari olumcul hata sanip betigi durdurur. Native cikis kodunu ($LASTEXITCODE)
# kendimiz kontrol ediyoruz.
$ErrorActionPreference = 'Continue'
$repo = $PSScriptRoot

# --- Context projeleri: proje yolu + DbContext sinif adi ---
$contexts = @(
    @{ Name = 'Editorial';  Project = 'src\Contexts\Editorial\ContentPlatform.Editorial';   Context = 'EditorialDbContext'  },
    @{ Name = 'Ingestion';  Project = 'src\Contexts\Ingestion\ContentPlatform.Ingestion';   Context = 'IngestionDbContext'  },
    @{ Name = 'Platform';   Project = 'src\Contexts\Platform\ContentPlatform.Platform';     Context = 'PlatformDbContext'   },
    @{ Name = 'Publishing'; Project = 'src\Contexts\Publishing\ContentPlatform.Publishing'; Context = 'PublishingDbContext' },
    @{ Name = 'Site';       Project = 'src\Contexts\Site\ContentPlatform.Site';             Context = 'SiteDbContext'       }
)

# --- 1) dotnet ef araci (yalnizca yoksa kur; surum sabitlemesi YOK) ---
Write-Host "==> 'dotnet ef' kontrol ediliyor..." -ForegroundColor Cyan
$efInstalled = (dotnet tool list -g 2>$null | Select-String 'dotnet-ef')
if (-not $efInstalled) {
    Write-Host "    kurulu degil - global kuruluyor (en son surum)..." -ForegroundColor Yellow
    dotnet tool install --global dotnet-ef | Out-Host
} else {
    Write-Host "    zaten kurulu." -ForegroundColor Green
}
$toolsPath = Join-Path $env:USERPROFILE ".dotnet\tools"
if ($env:PATH -notlike "*$toolsPath*") { $env:PATH = "$env:PATH;$toolsPath" }

# --- Yardimci: native ciktinin stderr'i hata sayilmasin; sadece exit kodu onemli ---
function Invoke-Ef {
    param([string[]]$EfArgs)
    # stderr'i stdout'a kat, hepsini goster; donus $LASTEXITCODE ile kontrol edilir
    & dotnet ef @EfArgs 2>&1 | Out-Host
    return $LASTEXITCODE
}

# --- 2) Her context icin drift kontrolu (+ uretim) ---
$drifted = @()
$clean   = @()
$failed  = @()

foreach ($c in $contexts) {
    $proj = Join-Path $repo $c.Project
    Write-Host ""
    Write-Host ("==> [{0}] kontrol ediliyor ({1})" -f $c.Name, $c.Context) -ForegroundColor Cyan

    # has-pending-model-changes: exit 0 = fark yok, exit != 0 = bekleyen degisiklik VAR.
    # Ciktisini tamamen yut (uyarilar dahil); sadece exit koduna bakariz.
    & dotnet ef migrations has-pending-model-changes --project $proj --startup-project $proj --context $c.Context *>$null
    $hasPending = ($LASTEXITCODE -ne 0)

    if (-not $hasPending) {
        Write-Host "    [OK] guncel - eksik migration yok." -ForegroundColor Green
        $clean += $c.Name
        continue
    }

    Write-Host "    [!] Bekleyen model degisikligi var (migration eksik)." -ForegroundColor Yellow
    $drifted += $c.Name
    if ($CheckOnly) { continue }

    $migName = "{0}_{1}" -f $c.Name, $Name
    Write-Host ("    -> migration uretiliyor: {0}" -f $migName) -ForegroundColor Yellow
    $code = Invoke-Ef @('migrations','add',$migName,'--project',$proj,'--startup-project',$proj,'--context',$c.Context,'--output-dir','Infrastructure\Migrations')
    if ($code -ne 0) { Write-Host "    [HATA] migration uretilemedi." -ForegroundColor Red; $failed += $c.Name; continue }

    if ($Update) {
        Write-Host "    -> yerel DB'ye uygulaniyor..." -ForegroundColor Yellow
        $code = Invoke-Ef @('database','update','--project',$proj,'--startup-project',$proj,'--context',$c.Context)
        if ($code -ne 0) { Write-Host "    [HATA] DB guncellenemedi." -ForegroundColor Red; $failed += $c.Name }
    }
}

# --- 3) Ozet ---
Write-Host ""
Write-Host "================ OZET ================" -ForegroundColor Cyan
Write-Host ("Guncel (fark yok) : {0}" -f ($clean   -join ', ')) -ForegroundColor Green
Write-Host ("Fark bulunan      : {0}" -f ($drifted -join ', ')) -ForegroundColor Yellow
if ($failed.Count) { Write-Host ("HATA alan         : {0}" -f ($failed -join ', ')) -ForegroundColor Red }

if ($CheckOnly) {
    Write-Host ""
    Write-Host "(CheckOnly modu - hicbir migration uretilmedi.)" -ForegroundColor DarkGray
} elseif ($drifted.Count -and -not $failed.Count) {
    Write-Host ""
    Write-Host "Migration'lar uretildi. Sirada: derle, sonra Worker ve Api deploy et." -ForegroundColor Cyan
    Write-Host "URETILEN migration dosyalarini GOZDEN GECIR (ozellikle 'may result in the loss" -ForegroundColor Yellow
    Write-Host "of data' uyarisi cikan Editorial): kolon genisletme (200->1000) guvenlidir," -ForegroundColor Yellow
    Write-Host "veri kaybi olmaz; ama beklenmedik DROP/ALTER var mi diye bak." -ForegroundColor Yellow
} else {
    Write-Host ""
    Write-Host "Eksik migration yok - her sey guncel." -ForegroundColor Green
}
