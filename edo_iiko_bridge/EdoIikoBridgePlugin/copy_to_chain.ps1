# Копирование плагина ЭДО ↔ iiko в папку Plugins iikoChain Office.
# Запускать из папки EdoIikoBridgePlugin после сборки (Release или Debug).

$ErrorActionPreference = "Stop"
$chainPlugins = "C:\Program Files\iiko\iikoChain\Office\Plugins"
$pluginFolder = "EdoIikoBridge"
$targetPath = Join-Path $chainPlugins $pluginFolder

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$buildDir = Join-Path $scriptDir "bin\Release"
if (-not (Test-Path $buildDir)) {
    $buildDir = Join-Path $scriptDir "bin\Debug"
}
if (-not (Test-Path $buildDir)) {
    Write-Error "Сначала соберите проект (Release или Debug). Папка не найдена: $scriptDir\bin"
}

$dll = Join-Path $buildDir "EdoIikoBridge.Plugin.dll"
$manifest = Join-Path $buildDir "Manifest.xml"
foreach ($f in @($dll, $manifest)) {
    if (-not (Test-Path $f)) {
        Write-Error "Не найден файл: $f"
    }
}

if (-not (Test-Path $chainPlugins)) {
    Write-Error "Папка iikoChain Office Plugins не найдена: $chainPlugins"
}

New-Item -ItemType Directory -Path $targetPath -Force | Out-Null
Copy-Item -Path $dll -Destination $targetPath -Force
Copy-Item -Path $manifest -Destination $targetPath -Force
Write-Host "Скопировано в $targetPath"
Write-Host "Перезапустите iikoChain Office. В меню дополнений должна появиться кнопка «ЭДО ↔ iiko»."
