<#
================================================================================
 ContentPlatform - Sunucu DISA CIKIS agi teshisi
--------------------------------------------------------------------------------
 NEDEN: Loglarda tum sosyal platformlar ayni hatayla dusuyor:
   SocketException 10060 (TCP connect timeout) -> api.telegram.org, api.x.com,
   graph.instagram.com, oauth2.googleapis.com :443. DNS calisiyor, sunucunun
   KENDI sitesi aciliyor, ama DIS host'lara 443 baglantisi kurulamiyor.
   Bu bir AG/FIREWALL sorunu (kod degil). Bu script nerede tikandigini gosterir.

 CALISTIR (sunucuda, RDP'den, yonetici PowerShell):
   powershell -ExecutionPolicy Bypass -File .\ag-testi.ps1
================================================================================
#>
$ErrorActionPreference = 'Continue'

# Platform host'lari + kontrol host'lari
$targets = @(
    'api.telegram.org',
    'oauth2.googleapis.com',
    'www.googleapis.com',
    'api.x.com',
    'graph.instagram.com',
    'graph.threads.net',
    'open.tiktokapis.com',
    'google.com',        # genel internet kontrolu
    '1.1.1.1'            # ham IP (DNS'siz) kontrolu
)

Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host " DISA CIKIS TESTI - her host icin DNS + TCP 443" -ForegroundColor Cyan
Write-Host "=====================================================" -ForegroundColor Cyan

$results = @()
foreach ($h in $targets) {
    $dnsOk = $true; $ip = ''
    if ($h -notmatch '^\d+\.\d+\.\d+\.\d+$') {
        try { $ip = (Resolve-DnsName -Name $h -Type A -ErrorAction Stop | Where-Object {$_.IPAddress} | Select-Object -First 1).IPAddress }
        catch { $dnsOk = $false }
    } else { $ip = $h }

    $tcpOk = $false; $ms = ''
    if ($dnsOk) {
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        try {
            $client = New-Object System.Net.Sockets.TcpClient
            $iar = $client.BeginConnect($h, 443, $null, $null)
            $tcpOk = $iar.AsyncWaitHandle.WaitOne(5000, $false) -and $client.Connected
            if ($tcpOk) { $client.EndConnect($iar) }
            $client.Close()
        } catch { $tcpOk = $false }
        $sw.Stop(); $ms = [int]$sw.ElapsedMilliseconds
    }

    $results += [pscustomobject]@{
        Host = $h; DNS = $(if($dnsOk){'OK'}else{'FAIL'}); IP = $ip
        'TCP443' = $(if($tcpOk){'ACIK'}else{'KAPALI/TIMEOUT'}); ms = $ms
    }
    $color = if ($tcpOk) { 'Green' } elseif (-not $dnsOk) { 'Magenta' } else { 'Red' }
    Write-Host ("  {0,-26} DNS:{1,-5} TCP443:{2,-16} {3}ms  {4}" -f `
        $h, $(if($dnsOk){'OK'}else{'FAIL'}), $(if($tcpOk){'ACIK'}else{'KAPALI'}), $ms, $ip) -ForegroundColor $color
}

Write-Host ""
Write-Host "=== Sistem proxy ayari (varsa app'i etkiler) ===" -ForegroundColor Cyan
netsh winhttp show proxy

Write-Host ""
Write-Host "=== ACIK outbound BLOCK firewall kurallari ===" -ForegroundColor Cyan
try {
    $blocks = Get-NetFirewallRule -Direction Outbound -Enabled True -Action Block -ErrorAction Stop
    if ($blocks) { $blocks | Select-Object DisplayName, DisplayGroup | Format-Table -Auto | Out-Host }
    else { Write-Host "  (Engelleyen outbound kural yok.)" -ForegroundColor Green }
} catch { Write-Host "  (Firewall kurallari okunamadi: $($_.Exception.Message))" -ForegroundColor DarkGray }

# --- Verdict ---
$ext = $results | Where-Object { $_.Host -in @('api.telegram.org','oauth2.googleapis.com','api.x.com','graph.instagram.com') }
$extOpen = ($ext | Where-Object { $_.'TCP443' -eq 'ACIK' }).Count
$ctrl = $results | Where-Object { $_.Host -eq 'google.com' }

Write-Host ""
Write-Host "================= SONUC =================" -ForegroundColor Cyan
if ($extOpen -eq 0) {
    Write-Host "TESHIS: Sunucu DIS host'lara 443 acamiyor (hicbir platform ACIK degil)." -ForegroundColor Red
    Write-Host "-> Bu bir AG/FIREWALL sorunu. Kod dogru; paylasim yapilamamasinin sebebi bu." -ForegroundColor Yellow
    Write-Host "   Olasi nedenler ve cozumler:" -ForegroundColor Yellow
    Write-Host "   1) Hosting/VPS saglayicisinin CIKIS (egress) firewall'i 443'u disariya kapatmis" -ForegroundColor Yellow
    Write-Host "      -> Saglayici panelinden outbound 443 (TCP) izni ac / guvenlik grubunu duzelt." -ForegroundColor Yellow
    Write-Host "   2) Windows Firewall outbound block kurali (yukaridaki listede goruntulendi mi?)" -ForegroundColor Yellow
    Write-Host "   3) Zorunlu bir proxy var ama uygulama kullanmiyor (yukaridaki proxy ciktisina bak)." -ForegroundColor Yellow
    Write-Host "   4) ISP/ulke bazli engel (TR'de X/Telegram sik; hepsi birden ise egress sorunu daha olasi)" -ForegroundColor Yellow
    Write-Host "      -> Cikis icin VPN/guvenilir proxy ya da farkli cikis IP'si gerekebilir." -ForegroundColor Yellow
} elseif ($extOpen -lt 4) {
    Write-Host "TESHIS: BAZI platformlar acik, bazilari kapali -> SECICI engel (muhtemelen ISP/ulke)." -ForegroundColor Yellow
    Write-Host "-> Kapali olan host'lar icin VPN/proxy ya da alternatif cikis gerekir." -ForegroundColor Yellow
} else {
    Write-Host "Dis host'lar SU AN acik. Sorun anlik/aralikli olabilir; hata aninda tekrar calistir." -ForegroundColor Green
}
Write-Host "Not: DNS FAIL yoksa isim cozme saglam; sorun yalniz TCP baglanti katmaninda." -ForegroundColor DarkGray
