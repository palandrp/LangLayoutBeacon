[CmdletBinding()]
param(
  [switch]$Publish,
  [switch]$Installer,
  [switch]$Clean,
  [switch]$All,
  [ValidateSet('fd-single','fd-multi','self-contained')]
  [string]$Mode = 'fd-single',
  [string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = $PSScriptRoot
$Proj = Join-Path $Root 'src\LangLayoutBeacon\LangLayoutBeacon.csproj'
$InstallerScript = Join-Path $Root 'LangLayoutBeacon.iss'

function Step([string]$m){ Write-Host "`n=== $m ===" -ForegroundColor Cyan }

function Get-ModeConfig([string]$mode) {
  switch ($mode) {
    'fd-single' {
      return @{
        PublishDir = Join-Path $Root 'build\publish-fd-single'
        OutputName = 'LangLayoutBeacon_setup_fd-single'
        PublishArgs = @('-r','win-x64','--self-contained','false','/p:PublishSingleFile=true','/p:IncludeNativeLibrariesForSelfExtract=false')
      }
    }
    'fd-multi' {
      return @{
        PublishDir = Join-Path $Root 'build\publish-fd-multi'
        OutputName = 'LangLayoutBeacon_setup_fd-multi'
        PublishArgs = @('-r','win-x64','--self-contained','false','/p:PublishSingleFile=false')
      }
    }
    'self-contained' {
      return @{
        PublishDir = Join-Path $Root 'build\publish-self-contained'
        OutputName = 'LangLayoutBeacon_setup_self-contained'
        PublishArgs = @('-r','win-x64','--self-contained','true','/p:PublishSingleFile=true','/p:IncludeNativeLibrariesForSelfExtract=true')
      }
    }
  }
}

function Do-Publish([hashtable]$cfg) {
  Step "Publish mode: $Mode"
  New-Item -ItemType Directory -Force -Path $cfg.PublishDir | Out-Null

  $args = @('publish', $Proj, '-c', $Configuration) + $cfg.PublishArgs + @('-o', $cfg.PublishDir)
  & dotnet @args
}

function Do-Installer([hashtable]$cfg) {
  Step "Build Inno Setup installer ($Mode)"
  $iscc = @(
    'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    'C:\Program Files\Inno Setup 6\ISCC.exe'
  ) | Where-Object { Test-Path $_ } | Select-Object -First 1

  if (-not $iscc) { throw 'ISCC.exe not found. Install Inno Setup 6.' }

  $exePath = Join-Path $cfg.PublishDir 'LangLayoutBeacon.exe'
  if (-not (Test-Path $exePath)) { throw "Published exe not found at $exePath. Run -Publish first." }

  & $iscc "/DPublishDir=$($cfg.PublishDir)" "/DOutputName=$($cfg.OutputName)" $InstallerScript
}

function Do-Clean {
  Step 'Clean build artifacts'
  Remove-Item -Recurse -Force (Join-Path $Root 'build') -ErrorAction SilentlyContinue
}

if ($All) { $Publish = $true; $Installer = $true }
if (-not ($Publish -or $Installer -or $Clean)) {
  Write-Host 'Use: -Publish -Installer -Clean or -All [-Mode fd-single|fd-multi|self-contained]' -ForegroundColor Yellow
  exit 1
}

$modeCfg = Get-ModeConfig $Mode
if ($Publish) { Do-Publish $modeCfg }
if ($Installer) { Do-Installer $modeCfg }
if ($Clean) { Do-Clean }
