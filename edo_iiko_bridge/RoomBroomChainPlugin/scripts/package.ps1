param(
  [string]$Configuration = "Release",
  [string]$OutDir = "",
  [string]$IikoChainLibDir = "",
  [switch]$Single = $false
)

$ErrorActionPreference = "Stop"

# По умолчанию собираем оба ZIP: для клиента (обфусцированный) и для себя (без обфускации).
# -Single — только один ZIP по указанной конфигурации (как раньше).
if (-not $Single) {
  $Configuration = "Release"
}

$projDir = Resolve-Path (Join-Path $PSScriptRoot "..")
$proj = Join-Path $projDir "RoomBroomChainPlugin.csproj"

if ([string]::IsNullOrWhiteSpace($OutDir)) {
  $OutDir = Join-Path $projDir "dist"
}

$canBuild = $true
if ([string]::IsNullOrWhiteSpace($IikoChainLibDir)) {
  $props = Join-Path $projDir "Directory.Build.props"
  if (-not (Test-Path $props)) {
    $canBuild = $false
  }
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function Build-Plugin {
  param([string]$Config)
  if (-not $canBuild) { return $null }
  $buildArgs = @("-c", $Config)
  if (-not [string]::IsNullOrWhiteSpace($IikoChainLibDir)) {
    $buildArgs += "/p:IikoChainLibDir=$IikoChainLibDir"
  }
  & dotnet build $proj @buildArgs | Out-Host
  if ($LASTEXITCODE -ne 0) { throw "Build failed for $Config" }
  Join-Path $projDir "bin\$Config\RoomBroomChainPlugin.dll"
}

function New-StageDir {
  $s = Join-Path $OutDir "RoomBroomChainPlugin_stage"
  if (Test-Path $s) { Remove-Item -Recurse -Force $s }
  New-Item -ItemType Directory -Force -Path $s | Out-Null
  return $s
}

function Copy-ToStage {
  param([string]$StageDir, [string]$config)
  $dll = Join-Path $projDir "bin\$config\RoomBroomChainPlugin.dll"
  if (-not (Test-Path $dll)) { throw "DLL not found: $dll" }
  Copy-Item $dll (Join-Path $StageDir "RoomBroomChainPlugin.dll") -Force
  $jsonDll = Join-Path $projDir "bin\$config\Newtonsoft.Json.dll"
  if (Test-Path $jsonDll) { Copy-Item $jsonDll (Join-Path $StageDir "Newtonsoft.Json.dll") -Force }
  $cfgJson = '{ "baseUrl": "", "login": "", "passwordSha1": "" }'
  Set-Content -Path (Join-Path $StageDir "RoomBroom.iiko.config.json") -Encoding UTF8 -Value $cfgJson
}

function Pack-Zip {
  param([string]$StageDir, [string]$ZipName)
  $zipPath = Join-Path $OutDir $ZipName
  if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
  Compress-Archive -Path (Join-Path $StageDir "*") -DestinationPath $zipPath -Force
  Write-Host "OK: $zipPath" -ForegroundColor Green
}

# --- Сборка и упаковка ---

if ($Single) {
  # One ZIP for selected configuration (Debug/Release)
  if ($Configuration -ne "Release") {
    Write-Host "Warning: for customer builds use: no -Single (two ZIPs) or -Single -Configuration Release." -ForegroundColor Yellow
  }

  if ($canBuild) {
    Write-Host ("Building plugin (" + $Configuration + ")...") -ForegroundColor Cyan
    $dll = Build-Plugin $Configuration
  } else {
    $dll = Join-Path $projDir ("bin\" + $Configuration + "\RoomBroomChainPlugin.dll")
    if (-not (Test-Path $dll)) {
      Write-Host "Directory.Build.props and DLL not found." -ForegroundColor Yellow
      exit 1
    }
  }

  $stage = New-StageDir
  Copy-ToStage $stage $Configuration
  Pack-Zip $stage 'RoomBroomChainPlugin.zip'
} else {
  # Two ZIPs: Release (obfuscated) for client, Debug (plain) for developer
  if (-not $canBuild) {
    Write-Host "Need Directory.Build.props to build both configurations. Or use -Single -Configuration Release." -ForegroundColor Yellow
    exit 1
  }

  Write-Host "Building Release (obfuscated)..." -ForegroundColor Cyan
  Build-Plugin "Release" | Out-Null
  $stageRel = New-StageDir
  Copy-ToStage $stageRel "Release"
  Pack-Zip $stageRel 'RoomBroomChainPlugin.zip'

  Write-Host "Building Debug (no obfuscation)..." -ForegroundColor Cyan
  Build-Plugin "Debug" | Out-Null
  $stageDev = New-StageDir
  Copy-ToStage $stageDev "Debug"
  Pack-Zip $stageDev 'RoomBroomChainPlugin-dev.zip'
}
