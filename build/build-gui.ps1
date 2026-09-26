<#
  Compiles the GUI with the .NET Framework compiler that ships with Windows (no SDK needed)
  and assembles the ready-to-run folder: RsyncGui.exe + bin\ (rsync, ssh, DLLs) + etc\ + licenses\.
  Run build-rsync.ps1 first so that <Work>\bundle exists.
#>
param(
    [string]$Out = (Join-Path $PSScriptRoot '..\dist\RsyncGui'),
    [string]$Work = (Join-Path $PSScriptRoot 'work'),
    [switch]$NoManifest      # test builds that must run without elevation
)
$ErrorActionPreference = 'Stop'
$Out = [IO.Path]::GetFullPath($Out)
$bundle = Join-Path ([IO.Path]::GetFullPath($Work)) 'bundle'
if (-not (Test-Path "$bundle\bin\rsync.exe")) { throw "Run build-rsync.ps1 first ($bundle\bin\rsync.exe is missing)" }

New-Item -ItemType Directory -Force $Out | Out-Null
Copy-Item "$bundle\*" $Out -Recurse -Force

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$src = Join-Path $PSScriptRoot '..\src'
$a = @('/nologo', '/target:winexe', '/optimize+', "/out:$Out\RsyncGui.exe",
       '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll',
       '/r:System.Web.Extensions.dll', '/r:System.Security.dll')
if (-not $NoManifest) { $a += "/win32manifest:$src\app.manifest" }
& $csc @a (Get-ChildItem "$src\*.cs" | ForEach-Object { $_.FullName })
if ($LASTEXITCODE -ne 0) { throw 'Compile failed' }
Copy-Item (Join-Path $PSScriptRoot '..\README.md') $Out -Force
Write-Host "Built $Out\RsyncGui.exe"
