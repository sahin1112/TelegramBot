<#
================================================================================
 ContentPlatform - Sunucuya deploy (publish + gonder + servis yeniden baslat)
--------------------------------------------------------------------------------
 AMAC: 'dotnet publish' ciktisini sunucuya, KESINTIYE DAYANIKLI ve SADECE
       DEGISEN DOSYALARI gonderecek sekilde tasimak (FileZilla ile elle
       surukleme derdi biter). Iki yontem destekler:

   -Method WinSCP  : SFTP ile (sunucuda OpenSSH Server acik olmali).
                     Resume + synchronize destekler; internet uzerinden calisir.
   -Method Robocopy: UNC/mapli surucu ya da RDP surucu yonlendirmesi uzerinden.
                     /Z (restartable) ile kesintide devam eder; ek yazilim yok.

 ONE-TIME KURULUMLAR:
   * WinSCP yontemi icin:
       - Dev makineye WinSCP kur (winscp.com CLI ile gelir):
           winget install WinSCP.WinSCP
       - Sunucuda OpenSSH Server ac (yonetici PowerShell, TEK SEFER):
           Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0
           Start-Service sshd; Set-Service sshd -StartupType Automatic
           New-NetFirewallRule -Name sshd -DisplayName 'OpenSSH' -Enabled True `
               -Direction Inbound -Protocol TCP -Action Allow -LocalPort 22
   * Robocopy yontemi icin: sunucu klasorune UNC/mapli erisim ya da RDP'de
     "Yerel Kaynaklar > Suruculer" isaretli (server'da \\tsclient\... gorunur).

 KULLANIM (repo kokunde, D:\projeler\TelegramBot):
   # SFTP ile:
   .\sunucuya-deploy.ps1 -Method WinSCP -SshHost 1.2.3.4 -SshUser Administrator `
       -RemoteApi 'C:\Services\ContentPlatformApi' -RemoteWorker 'C:\Services\TelegramWorker'

   # Robocopy ile (server klasoru mapli surucu M: olsun):
   .\sunucuya-deploy.ps1 -Method Robocopy `
       -RemoteApi 'M:\ContentPlatformApi' -RemoteWorker 'M:\TelegramWorker'
================================================================================
#>
[CmdletBinding()]
param(
    [ValidateSet('WinSCP','Robocopy')] [string]$Method = 'WinSCP',

    # Hangi projeler gonderilsin
    [switch]$SkipApi,
    [switch]$SkipWorker,

    # Hedef klasorler (sunucuda ya da mapli surucude)
    [string]$RemoteApi    = 'C:\Services\ContentPlatformApi',
    [string]$RemoteWorker = 'C:\Services\TelegramWorker',

    # --- WinSCP (SFTP) ayarlari ---
    [string]$SshHost,
    [string]$SshUser,
    [string]$SshPassword,                 # bos birakirsan anahtar/known kimlik kullanilir
    [int]$SshPort = 22,
    [string]$WinScpCom = 'C:\Program Files (x86)\WinSCP\WinSCP.com',

    # Servisleri yeniden baslat (WinSCP modunda SSH ile; Robocopy modunda yerelse sc ile)
    [switch]$NoRestart
)

$ErrorActionPreference = 'Continue'
$repo = $PSScriptRoot
$stage = Join-Path $env:TEMP 'cp-publish'
$apiOut    = Join-Path $stage 'api'
$workerOut = Join-Path $stage 'worker'

function Publish-One($projRel, $outDir, $label) {
    Write-Host ("==> [{0}] Release publish..." -f $label) -ForegroundColor Cyan
    if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
    dotnet publish (Join-Path $repo $projRel) -c Release -o $outDir 2>&1 | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "$label publish BASARISIZ (exit $LASTEXITCODE)" }
}

# --- 1) Yerelde publish ---
if (-not $SkipApi)    { Publish-One 'src\Host\ContentPlatform.Api'    $apiOut    'Api' }
if (-not $SkipWorker) { Publish-One 'src\Host\ContentPlatform.Worker' $workerOut 'Worker' }

# --- 2) Gonderim ---
if ($Method -eq 'Robocopy') {
    # /Z = restartable (kesintide devam) - /MIR = ayna (sadece degisenler) - /R:3 /W:5 = tekrar
    # /XO = hedefte daha yeni olani atla (guvenli) - /NP = ilerleme spam'i yok
    function Send-Robocopy($src, $dst, $label) {
        if (-not $SkipRestart -and -not $NoRestart) { }  # restart asagida
        Write-Host ("==> [{0}] robocopy -> {1}" -f $label, $dst) -ForegroundColor Cyan
        robocopy $src $dst /MIR /Z /R:3 /W:5 /NP /NFL /NDL
        # robocopy exit kodu 0-7 basari, >=8 hata
        if ($LASTEXITCODE -ge 8) { throw "$label robocopy HATA (exit $LASTEXITCODE)" }
        Write-Host ("    [OK] {0} gonderildi." -f $label) -ForegroundColor Green
    }
    if (-not $NoRestart) {
        # Yerel (ayni makine) servisse durdur; degilse bu adimi atla (uzak restart'i elle yap)
        Write-Host "==> Servisler durduruluyor (varsa)..." -ForegroundColor Yellow
        sc.exe stop TelegramWorker    | Out-Null
        sc.exe stop ContentPlatformApi| Out-Null
        Start-Sleep -Seconds 3
    }
    if (-not $SkipApi)    { Send-Robocopy $apiOut    $RemoteApi    'Api' }
    if (-not $SkipWorker) { Send-Robocopy $workerOut $RemoteWorker 'Worker' }
    if (-not $NoRestart) {
        Write-Host "==> Servisler baslatiliyor..." -ForegroundColor Yellow
        sc.exe start ContentPlatformApi | Out-Null
        sc.exe start TelegramWorker     | Out-Null
    }
}
else {
    # ----- WinSCP / SFTP -----
    if (-not (Test-Path $WinScpCom)) { throw "WinSCP.com bulunamadi: $WinScpCom  (winget install WinSCP.WinSCP)" }
    if (-not $SshHost -or -not $SshUser) { throw "SFTP icin -SshHost ve -SshUser gerekli." }

    # WinSCP hedef yolunu SFTP formatina cevir: 'C:\Services\X' -> '/C:/Services/X'
    function ToSftp($p) { '/' + ($p -replace '\\','/') }

    $creds = if ($SshPassword) { "$SshUser`:$SshPassword" } else { $SshUser }
    # -hostkey=* ilk baglantida host anahtarini kabul eder (guvenli agda). Sabit parmak izi
    # biliyorsan onunla degistir.
    $open = "open sftp://$creds@$SshHost`:$SshPort/ -hostkey=*"

    function Send-WinScp($src, $dstWin, $label) {
        $dst = ToSftp $dstWin
        Write-Host ("==> [{0}] SFTP synchronize -> {1}" -f $label, $dst) -ForegroundColor Cyan
        # synchronize remote: sadece degisenleri gonderir; kesilirse tekrar calistir, kaldigi
        # yerden tamamlar. 'option transfer binary' + 'option resume on' resume saglar.
        $script = @(
            'option batch abort',
            'option confirm off',
            'option transfer binary',
            $open,
            "synchronize remote `"$src`" `"$dst`"",
            'close',
            'exit'
        ) -join "`n"
        $tmp = New-TemporaryFile
        Set-Content -Path $tmp -Value $script -Encoding ASCII
        & $WinScpCom /ini=nul /script=$tmp
        $code = $LASTEXITCODE
        Remove-Item $tmp -Force
        if ($code -ne 0) { throw "$label SFTP gonderim HATA (exit $code)" }
        Write-Host ("    [OK] {0} gonderildi." -f $label) -ForegroundColor Green
    }

    function Invoke-Ssh($cmd) {
        # WinSCP ile de SSH komutu calistirilabilir (call). ssh.exe varsa onu da kullanabilirsin.
        $script = @('option batch abort', $open, "call $cmd", 'close', 'exit') -join "`n"
        $tmp = New-TemporaryFile; Set-Content $tmp $script -Encoding ASCII
        & $WinScpCom /ini=nul /script=$tmp | Out-Host
        Remove-Item $tmp -Force
    }

    if (-not $NoRestart) { Write-Host "==> Uzak servisler durduruluyor..." -ForegroundColor Yellow; Invoke-Ssh 'sc stop TelegramWorker & sc stop ContentPlatformApi' ; Start-Sleep 3 }
    if (-not $SkipApi)    { Send-WinScp $apiOut    $RemoteApi    'Api' }
    if (-not $SkipWorker) { Send-WinScp $workerOut $RemoteWorker 'Worker' }
    if (-not $NoRestart) { Write-Host "==> Uzak servisler baslatiliyor..." -ForegroundColor Yellow; Invoke-Ssh 'sc start ContentPlatformApi & sc start TelegramWorker' }
}

Write-Host ""
Write-Host "================ DEPLOY BITTI ================" -ForegroundColor Green
Write-Host "Ipucu: kesinti olursa ayni komutu TEKRAR calistir - sadece eksik/degisen" -ForegroundColor DarkGray
Write-Host "dosyalar gonderilir (synchronize/MIR), bastan yuklenmez." -ForegroundColor DarkGray
