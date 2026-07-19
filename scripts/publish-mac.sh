#!/usr/bin/env bash
# ContentPlatform — Mac'ten Windows sunucu icin publish (Api + Worker AYRI AYRI)
# Kullanim (repo kokunde):  bash scripts/publish-mac.sh
set -euo pipefail

# Repo koku (bu script scripts/ altinda)
cd "$(dirname "$0")/.."
ROOT="$(pwd)"
OUT="$ROOT/publish"
RID="win-x64"     # Sunucu Windows; SkiaSharp'in DOGRU native dll'i bu RID ile gelir
CONF="Release"

echo "==> .NET surumu (9.x olmali):"
dotnet --version

echo ""
echo "==> 1/3  Cozumu derle (kod hatalarini ERKEN yakala)"
dotnet restore "$ROOT/ContentPlatform.sln"
dotnet build   "$ROOT/ContentPlatform.sln" -c "$CONF" --no-restore

echo ""
echo "==> 2/3  API publish -> $OUT/api"
rm -rf "$OUT/api"
dotnet publish "$ROOT/src/Host/ContentPlatform.Api/ContentPlatform.Api.csproj" \
  -c "$CONF" -r "$RID" --self-contained false -o "$OUT/api"

echo ""
echo "==> 3/3  Worker publish -> $OUT/worker"
rm -rf "$OUT/worker"
dotnet publish "$ROOT/src/Host/ContentPlatform.Worker/ContentPlatform.Worker.csproj" \
  -c "$CONF" -r "$RID" --self-contained false -o "$OUT/worker"

echo ""
echo "======================================================"
echo " BITTI."
echo "  API    : $OUT/api      (calistirilabilir: ContentPlatform.Api.exe)"
echo "  Worker : $OUT/worker   (calistirilabilir: ContentPlatform.Worker.exe)"
echo ""
echo " Sunucuya kopyalarken KORU (silme/uzerine yazma):"
echo "   - sunucudaki  appsettings.json      (gercek baglanti/sifre)"
echo "   - API klasorundeki  wwwroot/media    (yuklenmis gorseller)"
echo "======================================================"
