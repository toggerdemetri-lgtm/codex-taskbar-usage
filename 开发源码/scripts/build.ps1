param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskPackages = Join-Path (Split-Path -Parent $taskRoot) '发布包'
New-Item -ItemType Directory -Path $taskPackages -Force | Out-Null
Push-Location -LiteralPath $taskRoot
try {
    if (!$SkipTests) { & (Join-Path $PSScriptRoot 'dotnet.ps1') run --project 'tests\CodexTaskbar.Tests' --configuration Release; if ($LASTEXITCODE -ne 0) { throw '测试失败' } }
    $taskPublish = Join-Path $taskRoot 'artifacts\CodexTaskbar'
    & (Join-Path $PSScriptRoot 'dotnet.ps1') publish 'src\CodexTaskbar' --configuration Release --runtime win-x64 --self-contained true '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' '-p:DebugType=None' --output $taskPublish
    if ($LASTEXITCODE -ne 0) { throw '发布失败' }
    Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md') -Destination $taskPublish -Force
    $taskPlugin = Join-Path $taskRoot 'artifacts\codex-taskbar-plugin'
    New-Item -ItemType Directory -Path $taskPlugin -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $taskRoot 'plugin') -Force | Copy-Item -Destination $taskPlugin -Recurse -Force
    $taskPluginBin = Join-Path $taskPlugin 'bin\win-x64'
    New-Item -ItemType Directory -Path $taskPluginBin -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $taskPublish 'CodexTaskbar.exe') -Destination $taskPluginBin -Force
    Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md') -Destination $taskPlugin -Force
    Compress-Archive -Path $taskPublish -DestinationPath (Join-Path $taskPackages 'Codex任务栏用量-便携版-Windows-x64.zip') -Force
    # Include the hidden compatibility manifest; Compress-Archive can omit hidden items.
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $taskPluginZip = Join-Path $taskPackages 'Codex任务栏用量-Codex插件版-Windows-x64.zip'
    if (Test-Path -LiteralPath $taskPluginZip) { Remove-Item -LiteralPath $taskPluginZip -Force }
    [System.IO.Compression.ZipFile]::CreateFromDirectory($taskPlugin,$taskPluginZip)
    & (Join-Path $PSScriptRoot 'package-source.ps1')
    Write-Output $taskPublish
    Write-Output $taskPluginZip
} finally { Pop-Location }
