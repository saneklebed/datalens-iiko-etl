param(
  [string]$Configuration = "Release",
  [string]$OutDir = "$(Resolve-Path (Join-Path $PSScriptRoot "..\dist"))",
  [string]$IikoChainLibDir = ""
)

$ErrorActionPreference = "Stop"

$projDir = Resolve-Path (Join-Path $PSScriptRoot "..")
$proj = Join-Path $projDir "RoomBroomChainPlugin.csproj"

if ([string]::IsNullOrWhiteSpace($IikoChainLibDir)) {
  $props = Join-Path $projDir "Directory.Build.props"
  if (-not (Test-Path $props)) {
    Write-Host "Не найден Directory.Build.props." -ForegroundColor Yellow
    Write-Host "Скопируй Directory.Build.props.example -> Directory.Build.props и укажи IikoChainLibDir" -ForegroundColor Yellow
    Write-Host "Либо передай параметр: -IikoChainLibDir `"C:\Program Files\iiko\iikoChain\Office`"" -ForegroundColor Yellow
    exit 1
  }
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

Write-Host "Building plugin..." -ForegroundColor Cyan
if ([string]::IsNullOrWhiteSpace($IikoChainLibDir)) {
  dotnet build $proj -c $Configuration | Out-Host
} else {
  dotnet build $proj -c $Configuration "/p:IikoChainLibDir=$IikoChainLibDir" | Out-Host
}

$dll = Join-Path $projDir "bin\$Configuration\RoomBroomChainPlugin.dll"
if (-not (Test-Path $dll)) {
  throw "Не найден DLL после сборки: $dll"
}

# Стейджинг под ZIP: содержимое, которое нужно распаковать в Office\Plugins
$stage = Join-Path $OutDir "RoomBroomChainPlugin"
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $stage
New-Item -ItemType Directory -Force -Path $stage | Out-Null

Copy-Item $dll (Join-Path $stage "RoomBroomChainPlugin.dll") -Force

# (опционально) папка под логи/конфиги, если позже понадобится
$logsDir = Join-Path $stage "RoomBroom"
New-Item -ItemType Directory -Force -Path $logsDir | Out-Null
Set-Content -Path (Join-Path $logsDir "readme.txt") -Encoding UTF8 -Value @"
Эта папка зарезервирована под логи/настройки плагина RoomBroom.
Плагин в дальнейшем может писать сюда данные, аналогично EDI-Doc:
  AppDomain.CurrentDomain.BaseDirectory + 'Plugins\RoomBroom\'
"@

$zip = Join-Path $OutDir "RoomBroomChainPlugin.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }

Write-Host "Packing ZIP..." -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -Force

Write-Host "OK: $zip" -ForegroundColor Green
Write-Host "Распакуй содержимое ZIP в:" -ForegroundColor Green
Write-Host "  C:\Program Files\iiko\iikoChain\Office\Plugins" -ForegroundColor Green

