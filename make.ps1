[CmdletBinding()]
param(
  [switch]$Publish,
  [switch]$Installer,
  [switch]$Clean,
  [switch]$All,
  [string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = $PSScriptRoot
$Proj = Join-Path $Root 'src\LangLayoutBeacon\LangLayoutBeacon.csproj'
$PublishDir = Join-Path $Root 'build\publish'
$InstallerScript = Join-Path $Root 'LangLayoutBeacon.iss'

function Step([string]$m){ Write-Host "`n=== $m ===" -ForegroundColor Cyan }

function Do-Publish {
  Step 'Publish self-contained Win-x64 app'
  dotnet publish $Proj -c $Configuration -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o $PublishDir
}

function Do-Installer {
  Step 'Build Inno Setup installer'
  $iscc = @(
    'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    'C:\Program Files\Inno Setup 6\ISCC.exe'
  ) | Where-Object { Test-Path $_ } | Select-Object -First 1

  if (-not $iscc) { throw 'ISCC.exe not found. Install Inno Setup 6.' }
  if (-not (Test-Path (Join-Path $PublishDir 'LangLayoutBeacon.exe'))) { throw 'Published exe not found. Run -Publish first.' }

  & $iscc $InstallerScript
}

function Do-Clean {
  Step 'Clean build artifacts'
  Remove-Item -Recurse -Force (Join-Path $Root 'build') -ErrorAction SilentlyContinue
}

if ($All) { $Publish = $true; $Installer = $true }
if (-not ($Publish -or $Installer -or $Clean)) {
  Write-Host 'Use: -Publish -Installer -Clean or -All' -ForegroundColor Yellow
  exit 1
}

if ($Publish) { Do-Publish }
if ($Installer) { Do-Installer }
if ($Clean) { Do-Clean }
