<#
  Builds the real rsync from https://github.com/RsyncProject/rsync under Cygwin and
  collects rsync.exe, ssh.exe, ssh-keygen.exe and their DLLs into <Work>\bundle.
  Cygwin setup asks for elevation once. Needs internet access. Takes about 10 minutes.
#>
param(
    [string]$Work = (Join-Path $PSScriptRoot 'work'),
    [string]$Tag = 'v3.5.1',
    [string]$Mirror = 'https://mirrors.kernel.org/sourceware/cygwin/'
)
$ErrorActionPreference = 'Stop'
$Work = [IO.Path]::GetFullPath($Work)
$root = Join-Path $Work 'cyg'
$pk = Join-Path $Work 'cygpk'
$bundle = Join-Path $Work 'bundle'
New-Item -ItemType Directory -Force $Work, $root, $pk | Out-Null

# Runs a shell script inside Cygwin. Windows PowerShell 5.1 turns native stderr output into a
# terminating error under $ErrorActionPreference = 'Stop', so relax it for the call and check the exit code.
function Invoke-CygBash([string]$scriptFile) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $lines = & "$root\bin\bash.exe" -lc ("bash " + (ToCygPath $scriptFile)) 2>&1 | ForEach-Object { "$_" }
    $code = $LASTEXITCODE
    $ErrorActionPreference = $old
    return [pscustomobject]@{ Code = $code; Lines = $lines }
}

function ToCygPath([string]$p) {
    $p = $p -replace '\\', '/'
    if ($p -match '^([A-Za-z]):(.*)$') { return '/cygdrive/' + $Matches[1].ToLower() + $Matches[2] }
    return $p
}

# 1. Cygwin with the build tools and the libraries rsync links against
$pkgs = 'gcc-core,make,autoconf,automake,libtool,perl,libpopt-devel,zlib-devel,libxxhash-devel,libzstd-devel,liblz4-devel,libattr-devel,libacl-devel,git,gawk,sed,grep,diffutils,patch,m4,pkg-config,bzip2,tar,openssh'
if (-not (Test-Path "$root\bin\gcc.exe") -or -not (Test-Path "$root\bin\ssh.exe")) {
    $setup = Join-Path $Work 'setup-x86_64.exe'
    if (-not (Test-Path $setup)) { Invoke-WebRequest https://cygwin.com/setup-x86_64.exe -OutFile $setup -UseBasicParsing }
    Start-Process -FilePath $setup -Wait -NoNewWindow -ArgumentList "-q -n -N -d -R `"$root`" -l `"$pk`" -s $Mirror -P $pkgs"
    if (-not (Test-Path "$root\bin\gcc.exe")) { throw 'Cygwin install failed' }
}

# 2. Clone the official tag and build
$sh = @"
set -e
cd /tmp
if [ ! -d rsync ]; then git clone -q --depth 1 --branch $Tag https://github.com/RsyncProject/rsync.git rsync; fi
cd rsync
if [ ! -x rsync.exe ]; then
  ./configure --disable-md2man --disable-openssl --disable-locale --disable-idn >/tmp/configure.log 2>&1 || { tail -30 /tmp/configure.log; exit 1; }
  make -j8 >/tmp/make.log 2>&1 || { tail -40 /tmp/make.log; exit 1; }
fi
./rsync.exe --version | head -1
git log -1 --format='commit %H' > /tmp/rsync-commit.txt
"@
$shFile = Join-Path $Work 'build-rsync.sh'
[IO.File]::WriteAllText($shFile, ($sh -replace "`r`n", "`n"))
$r = Invoke-CygBash $shFile
$r.Lines | Select-Object -Last 3 | ForEach-Object { Write-Host $_ }
if ($r.Code -ne 0) { $r.Lines | Select-Object -Last 40 | ForEach-Object { Write-Host $_ }; throw 'rsync build failed' }

# 3. Collect the runtime files
if (Test-Path $bundle) { Remove-Item $bundle -Recurse -Force }
New-Item -ItemType Directory -Force "$bundle\bin", "$bundle\etc", "$bundle\tmp", "$bundle\licenses" | Out-Null
$collect = @'
set -e
for f in /tmp/rsync/rsync.exe /usr/bin/ssh.exe /usr/bin/ssh-keygen.exe; do
  echo $f
  ldd $f | awk '/=>/ {print $3}' | grep -v -i '^/cygdrive/c/[Ww]indows'
done | sort -u
'@
$colFile = Join-Path $Work 'collect.sh'
[IO.File]::WriteAllText($colFile, ($collect -replace "`r`n", "`n"))
$c = Invoke-CygBash $colFile
if ($c.Code -ne 0) { throw 'Could not list the runtime DLLs' }
$files = $c.Lines | Where-Object { $_ -like '/*' }
foreach ($f in $files) {
    $f = $f.Trim()
    if ($f -eq '/tmp/rsync/rsync.exe') { Copy-Item "$root\tmp\rsync\rsync.exe" "$bundle\bin\" }
    elseif ($f -like '/usr/bin/*') { Copy-Item (Join-Path "$root\bin" (Split-Path $f -Leaf)) "$bundle\bin\" }
}
[IO.File]::WriteAllText("$bundle\etc\fstab",
    "# Rsync GUI: no POSIX ACL emulation on Windows drives, so destination files keep normal Windows permissions.`n" +
    "none /cygdrive cygdrive binary,posix=0,user,noacl 0 0`n")
Copy-Item "$root\tmp\rsync\COPYING" "$bundle\licenses\rsync-COPYING.txt"
$commit = (Get-Content "$root\tmp\rsync-commit.txt") -join ''
$ver = (& "$bundle\bin\rsync.exe" --version | Select-Object -First 1)
@"
Rsync GUI bundles unmodified builds of these programs.

rsync        $ver
             Source: https://github.com/RsyncProject/rsync  tag $Tag  $commit
             Licence: GNU GPL v3 (licenses\rsync-COPYING.txt)
             Configured with: --disable-md2man --disable-openssl --disable-locale --disable-idn

Cygwin runtime, OpenSSH (ssh.exe, ssh-keygen.exe) and the DLLs in bin\
             Source and licences: https://cygwin.com/ and https://www.openssh.com/
             Cygwin package sources: https://cygwin.com/packages/  (mirror: $Mirror)
"@ | Set-Content -Encoding UTF8 "$bundle\licenses\SOURCES.txt"
Write-Host "Bundle ready: $bundle"
