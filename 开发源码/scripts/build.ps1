param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskPackages = Join-Path (Split-Path -Parent $taskRoot) '发布包'
New-Item -ItemType Directory -Path $taskPackages -Force | Out-Null
Push-Location -LiteralPath $taskRoot
try {
    if (!$SkipTests) { & (Join-Path $PSScriptRoot 'dotnet.ps1') run --project 'tests\CodexTaskbar.Tests' --configuration Release; if ($LASTEXITCODE -ne 0) { throw '测试失败' } }
    if (!$SkipTests) { & (Join-Path $PSScriptRoot 'dotnet.ps1') run --project 'tests\CodexTaskbar.WindowsTests' --configuration Release; if ($LASTEXITCODE -ne 0) { throw 'Windows原生集成测试失败' } }
    $taskPublish = Join-Path $taskRoot 'artifacts\Codex用量任务栏'
    & (Join-Path $PSScriptRoot 'dotnet.ps1') publish 'src\CodexTaskbar' --configuration Release --runtime win-x64 --self-contained true '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' '-p:DebugType=None' --output $taskPublish
    if ($LASTEXITCODE -ne 0) { throw '发布失败' }
    Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md') -Destination $taskPublish -Force
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs\images\taskbar.png') -Destination (Join-Path $taskPublish '效果截图.png') -Force
    Compress-Archive -Path $taskPublish -DestinationPath (Join-Path $taskPackages 'Codex用量任务栏-便携版-Windows10-11-x64.zip') -Force
    & (Join-Path $taskRoot 'scripts\package-source.ps1')
    $taskArchiveNames = @('Codex用量任务栏-便携版-Windows10-11-x64.zip','Codex用量任务栏-完整源码.zip')
    $taskChecksums = foreach ($taskArchiveName in $taskArchiveNames) {
        $taskHash = (Get-FileHash -LiteralPath (Join-Path $taskPackages $taskArchiveName) -Algorithm SHA256).Hash.ToLowerInvariant()
        "$taskHash  $taskArchiveName"
    }
    [IO.File]::WriteAllText((Join-Path $taskPackages 'SHA256SUMS.txt'),(($taskChecksums -join "`n")+"`n"),[Text.UTF8Encoding]::new($false))
    Write-Output $taskPublish
    Write-Output (Join-Path $taskPackages 'SHA256SUMS.txt')
} finally { Pop-Location }
