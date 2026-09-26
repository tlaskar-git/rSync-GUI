<#
  Adds the official rclone release to <Work>\bundle (run build-rsync.ps1 first, and again whenever it is re-run,
  because that script recreates the bundle folder). Downloads from the rclone GitHub release and
  checks the SHA-256 of the zip against the SHA256SUMS file published with that release.
#>
param(
    [string]$Work = (Join-Path $PSScriptRoot 'work'),
    [string]$Version = 'v1.75.1'
)
$ErrorActionPreference = 'Stop'
$Work = [IO.Path]::GetFullPath($Work)
$bundle = Join-Path $Work 'bundle'
if (-not (Test-Path "$bundle\bin")) { throw "Run build-rsync.ps1 first ($bundle\bin is missing)" }

$base = "https://github.com/rclone/rclone/releases/download/$Version"
$zipName = "rclone-$Version-windows-amd64.zip"
$dl = Join-Path $Work 'rclone-dl'
New-Item -ItemType Directory -Force $dl | Out-Null
$zip = Join-Path $dl $zipName
if (-not (Test-Path $zip)) { Invoke-WebRequest "$base/$zipName" -OutFile $zip -UseBasicParsing }
Invoke-WebRequest "$base/SHA256SUMS" -OutFile "$dl\SHA256SUMS" -UseBasicParsing
$want = ((Select-String -Path "$dl\SHA256SUMS" -Pattern ([regex]::Escape($zipName))).Line -split '\s+')[0]
$got = (Get-FileHash $zip -Algorithm SHA256).Hash
if ($want -ine $got) { throw "Checksum mismatch for $zipName (want $want, got $got)" }

$x = Join-Path $dl 'x'
if (Test-Path $x) { Remove-Item $x -Recurse -Force }
Expand-Archive $zip $x -Force
$root = Get-ChildItem $x -Directory | Select-Object -First 1
Copy-Item "$($root.FullName)\rclone.exe" "$bundle\bin\rclone.exe" -Force
Invoke-WebRequest "https://raw.githubusercontent.com/rclone/rclone/$Version/COPYING" -OutFile "$bundle\licenses\rclone-LICENSE.txt" -UseBasicParsing
$ver = (& "$bundle\bin\rclone.exe" version | Select-Object -First 1)
$notes = "$bundle\licenses\SOURCES.txt"
if (-not (Select-String -Path $notes -Pattern '^rclone ' -Quiet)) {
    Add-Content -Encoding UTF8 $notes @"

rclone       $ver
             Source: https://github.com/rclone/rclone  release $Version (unmodified official Windows build, SHA-256 checked)
             Licence: MIT (licenses\rclone-LICENSE.txt)
"@
}
Write-Host "Added $ver to $bundle"
