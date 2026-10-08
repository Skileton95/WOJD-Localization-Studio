param(
    [Parameter(Mandatory = $true)]
    [string]$UpdaterExe
)

$ErrorActionPreference = 'Stop'

$updater = (Resolve-Path $UpdaterExe).Path
$root = Join-Path $env:RUNNER_TEMP ("wojd-updater-smoke-" + [Guid]::NewGuid().ToString('N'))
$install = Join-Path $root 'install'
$payload = Join-Path $root 'payload'
$zip = Join-Path $root 'package.zip'

New-Item -ItemType Directory -Force -Path $install, $payload | Out-Null
Set-Content -Path (Join-Path $install 'version.txt') -Value '0.0.0' -NoNewline
Set-Content -Path (Join-Path $install 'marker.txt') -Value 'old' -NoNewline

Set-Content -Path (Join-Path $payload 'version.txt') -Value '9.9.9' -NoNewline
Set-Content -Path (Join-Path $payload 'marker.txt') -Value 'updated' -NoNewline
New-Item -ItemType Directory -Force -Path (Join-Path $payload 'nested') | Out-Null
Set-Content -Path (Join-Path $payload 'nested\proof.txt') -Value 'ok' -NoNewline

Compress-Archive -Path (Join-Path $payload '*') -DestinationPath $zip -Force

Write-Host "Starting updater smoke test from $updater"
& $updater --install-dir $install --package-file $zip --version 9.9.9
if ($LASTEXITCODE -ne 0) {
    throw "Updater staging process exited with code $LASTEXITCODE"
}

$deadline = (Get-Date).AddSeconds(30)
$installed = $false
while ((Get-Date) -lt $deadline) {
    $versionPath = Join-Path $install 'version.txt'
    $markerPath = Join-Path $install 'marker.txt'

    if ((Test-Path $versionPath) -and (Test-Path $markerPath)) {
        $version = (Get-Content $versionPath -Raw).Trim()
        $marker = (Get-Content $markerPath -Raw).Trim()
        if ($version -eq '9.9.9' -and $marker -eq 'updated') {
            $installed = $true
            break
        }
    }

    Start-Sleep -Milliseconds 250
}

if (-not $installed) {
    $logPath = Join-Path $install 'update.log'
    if (Test-Path $logPath) {
        Write-Host '--- update.log ---'
        Get-Content $logPath
        Write-Host '------------------'
    }
    throw 'Updater did not apply the package within 30 seconds.'
}

$proofPath = Join-Path $install 'nested\proof.txt'
if (-not (Test-Path $proofPath) -or ((Get-Content $proofPath -Raw).Trim() -ne 'ok')) {
    throw 'Nested package file was not applied.'
}

Start-Sleep -Milliseconds 500
$logPath = Join-Path $install 'update.log'
if (-not (Test-Path $logPath)) {
    throw 'Updater did not create update.log.'
}

$log = Get-Content $logPath -Raw
if ($log -notmatch 'Applied local package 9\.9\.9') {
    Write-Host $log
    throw 'Updater did not record successful package application.'
}

Write-Host 'Updater smoke test passed.'

try {
    Remove-Item $root -Recurse -Force -ErrorAction Stop
}
catch {
    Write-Warning "Smoke-test cleanup skipped: $($_.Exception.Message)"
}
