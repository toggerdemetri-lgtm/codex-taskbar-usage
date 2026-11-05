$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskPackages = Join-Path (Split-Path -Parent $taskRoot) '发布包'
New-Item -ItemType Directory -Path $taskPackages -Force | Out-Null
$taskZipPath = Join-Path $taskPackages 'Codex任务栏用量-完整源码.zip'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Push-Location -LiteralPath $taskRoot
try {
    $taskFiles = & rg --files --hidden -g '!.git/**' -g '!.tools/**' -g '!artifacts/**' -g '!**/bin/**' -g '!**/obj/**'
    if ($LASTEXITCODE -ne 0) { throw '无法枚举源码文件，需要安装 ripgrep' }
    if (Test-Path -LiteralPath $taskZipPath) { Remove-Item -LiteralPath $taskZipPath -Force }
    $taskZip = [System.IO.Compression.ZipFile]::Open($taskZipPath,[System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($taskRelative in $taskFiles) {
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskZip,(Join-Path $taskRoot $taskRelative),$taskRelative.Replace('\','/'),[System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $taskZip.Dispose() }
    Write-Output $taskZipPath
} finally { Pop-Location }
