param([ValidateSet('Start','Stop','Status')][string]$Action = 'Status')
$ErrorActionPreference = 'Stop'
$taskPluginRoot = Split-Path -Parent $PSScriptRoot
$taskExe = Join-Path $taskPluginRoot 'bin\win-x64\Codex用量任务栏.exe'
if (!(Test-Path -LiteralPath $taskExe)) { throw '程序尚未打包。请先运行源码目录 scripts\build.ps1，或使用完整发布 ZIP。' }
if ($Action -eq 'Start') {
    Start-Process -FilePath $taskExe -ArgumentList '--start' -WorkingDirectory (Split-Path -Parent $taskExe) -WindowStyle Hidden
    Start-Sleep -Milliseconds 1200
}
$taskOutput = [System.IO.Path]::GetTempFileName()
try {
    $taskCommand = if ($Action -eq 'Stop') { '--stop' } else { '--status' }
    $taskArguments = @($taskCommand, '--output', ('"' + $taskOutput + '"'))
    $taskProcess = Start-Process -FilePath $taskExe -ArgumentList $taskArguments -WindowStyle Hidden -Wait -PassThru
    if ($taskProcess.ExitCode -ne 0) { throw ('组件命令失败，退出码 ' + $taskProcess.ExitCode) }
    Get-Content -LiteralPath $taskOutput -Raw
} finally { Remove-Item -LiteralPath $taskOutput -Force -ErrorAction SilentlyContinue }
