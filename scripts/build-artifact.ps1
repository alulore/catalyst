$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root 'dist'

if (-not (Get-Command vswhere.exe -ErrorAction SilentlyContinue)) {
    $installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
    if (-not (Test-Path (Join-Path $installer 'vswhere.exe'))) {
        throw "vswhere.exe was not found in $installer. Install Visual Studio Build Tools with the C++ workload."
    }
    $env:PATH = "$installer;$env:PATH"
}

if (Test-Path $dist) {
    Remove-Item -Recurse -Force $dist
}

dotnet publish (Join-Path $root 'Catalyst.csproj') --configuration Release --runtime win-x64 -p:PublishAot=true --output $dist --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$exe = Join-Path $dist 'Catalyst.exe'
if (-not (Test-Path $exe)) {
    throw "The publish did not produce $exe."
}
