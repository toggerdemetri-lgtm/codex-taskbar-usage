param(
    [string]$Executable = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) '运行程序\CodexTaskbar.exe'),
    [switch]$RestartExplorer
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskOutputDir = Join-Path $taskRoot 'artifacts\verification'
New-Item -ItemType Directory -Path $taskOutputDir -Force | Out-Null
$taskExe = (Resolve-Path -LiteralPath $Executable).Path
function Invoke-TaskbarControl([string]$Command) {
    $taskReply = Join-Path $taskOutputDir ('.ipc-' + [guid]::NewGuid().ToString('N') + '.json')
    try {
        $taskChild = Start-Process -FilePath $taskExe -ArgumentList @($Command,'--output',('"'+$taskReply+'"')) -WindowStyle Hidden -Wait -PassThru
        if ($taskChild.ExitCode -ne 0) { throw ('命令失败：'+$Command) }
        return Get-Content -LiteralPath $taskReply -Raw | ConvertFrom-Json
    } finally { if (Test-Path -LiteralPath $taskReply) { Remove-Item -LiteralPath $taskReply -Force } }
}
function Wait-TaskbarReady {
    $taskDeadline = [DateTime]::UtcNow.AddSeconds(35)
    do {
        $taskState = Invoke-TaskbarControl '--status'
        if ($taskState.running -and $taskState.attached -and $null -ne $taskState.totalTokens -and $null -ne $taskState.lastQuotaUpdate -and !$taskState.stale) { return $taskState }
        Start-Sleep -Seconds 1
    } while ([DateTime]::UtcNow -lt $taskDeadline)
    throw ('组件未就绪：'+($taskState | ConvertTo-Json -Compress))
}

$taskStop = Invoke-TaskbarControl '--stop'
Start-Sleep -Milliseconds 500
$taskStopped = Invoke-TaskbarControl '--status'
if ($taskStopped.running) { throw '停止命令后仍有实例' }
Start-Process -FilePath $taskExe -ArgumentList '--start' -WindowStyle Hidden
$taskBefore = Wait-TaskbarReady
if ($taskBefore.parentClass -ne 'Shell_TrayWnd' -or !$taskBefore.visible -or !$taskBefore.textFits) { throw '原生嵌入或可见文本验收失败' }
if (@($taskBefore.scales | Where-Object { !$_.textFits }).Count -ne 0) { throw '缩放文字宽度检查失败' }
Start-Process -FilePath $taskExe -ArgumentList '--start' -WindowStyle Hidden -Wait
$taskDuplicate = Invoke-TaskbarControl '--status'
if ($taskDuplicate.pid -ne $taskBefore.pid) { throw '重复启动未复用原实例' }
$taskBefore | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $taskOutputDir 'before.json') -Encoding utf8

$taskRestartResult = 'not requested'
if ($RestartExplorer) {
    $taskSessionId = [System.Diagnostics.Process]::GetCurrentProcess().SessionId
    $taskExplorerPath = Join-Path $env:WINDIR 'explorer.exe'
    try {
        Get-Process -Name explorer -ErrorAction Stop | Where-Object { $_.SessionId -eq $taskSessionId -and $_.Path -eq $taskExplorerPath } | Stop-Process
        Start-Sleep -Seconds 3
        if (!(Get-Process -Name explorer -ErrorAction SilentlyContinue | Where-Object { $_.SessionId -eq $taskSessionId })) {
            Start-Process -FilePath $taskExplorerPath -WindowStyle Hidden
        }
        $taskAfter = Wait-TaskbarReady
        if ($taskAfter.pid -ne $taskBefore.pid) { throw 'Explorer 重启后组件退出，未保持进程' }
        if ($taskAfter.parent -eq $taskBefore.parent) { throw '未观察到任务栏父窗口重建' }
        $taskAfter | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $taskOutputDir 'after-explorer.json') -Encoding utf8
        $taskRestartResult = 'passed'
    } finally {
        if (!(Get-Process -Name explorer -ErrorAction SilentlyContinue | Where-Object { $_.SessionId -eq $taskSessionId })) { Start-Process -FilePath $taskExplorerPath -WindowStyle Hidden }
        $taskRecovery = Invoke-TaskbarControl '--status'
        if (!$taskRecovery.running) { Start-Process -FilePath $taskExe -ArgumentList '--start' -WindowStyle Hidden }
    }
}
$taskScreenshot = Join-Path $taskOutputDir 'taskbar.png'
Start-Process -FilePath $taskExe -ArgumentList @('--capture','--output',('"'+$taskScreenshot+'"')) -WindowStyle Hidden -Wait
if (!(Test-Path -LiteralPath $taskScreenshot)) { throw '实际任务栏截图未生成' }
$taskFinal = Invoke-TaskbarControl '--status'
$taskFinal | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $taskOutputDir 'final-status.json') -Encoding utf8
[pscustomobject]@{nativeEmbedding='passed';liveQuota='passed';liveTokens='passed';singleInstance='passed';stopStart='passed';textWidth100to200='passed';explorerRestart=$taskRestartResult;screenshot=$taskScreenshot;pid=$taskFinal.pid} | ConvertTo-Json | Tee-Object -FilePath (Join-Path $taskOutputDir 'smoke-result.json')
