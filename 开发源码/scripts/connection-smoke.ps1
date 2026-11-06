$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskExe = Join-Path (Split-Path -Parent $taskRoot) '运行程序\Codex用量任务栏.exe'
$taskOutput = Join-Path $taskRoot 'artifacts\verification'
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
function Read-ConnectionState {
    $taskReply = Join-Path $taskOutput 'connection-state.json'
    Start-Process -FilePath $taskExe -ArgumentList '--status','--output',('"'+$taskReply+'"') -WindowStyle Hidden -Wait
    Get-Content -LiteralPath $taskReply -Raw | ConvertFrom-Json
}
Start-Process -FilePath $taskExe -ArgumentList '--stop' -WindowStyle Hidden -Wait
Start-Sleep -Milliseconds 500
$taskSaved = @{}
foreach ($taskKey in @('HTTP_PROXY','HTTPS_PROXY','ALL_PROXY','NO_PROXY')) { $taskSaved[$taskKey]=[Environment]::GetEnvironmentVariable($taskKey,'Process') }
try {
    try {
        $env:HTTP_PROXY='http://127.0.0.1:9'; $env:HTTPS_PROXY='http://127.0.0.1:9'; $env:ALL_PROXY='http://127.0.0.1:9'; $env:NO_PROXY=''
        Start-Process -FilePath $taskExe -ArgumentList '--start' -WindowStyle Hidden
    } finally { foreach ($taskKey in $taskSaved.Keys) { [Environment]::SetEnvironmentVariable($taskKey,$taskSaved[$taskKey],'Process') } }
    $taskDeadline=[DateTime]::UtcNow.AddSeconds(35)
    do {
        Start-Sleep -Seconds 1
        $taskOffline=Read-ConnectionState
        # Initialization notifications are not evidence of a failed network query.
        if ($taskOffline.quotaError -and $taskOffline.quotaError -notmatch '正在') { break }
    } while ([DateTime]::UtcNow -lt $taskDeadline)
    $taskOffline | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $taskOutput 'offline.json') -Encoding utf8
    if (!$taskOffline.quotaError -or $taskOffline.quotaError -match '正在' -or $taskOffline.lastQuotaUpdate) { throw '未观察到实际离线查询失败' }
    Write-Output ('离线查询失败状态已验证：'+$taskOffline.quotaError)
} finally {
    Start-Process -FilePath $taskExe -ArgumentList '--stop' -WindowStyle Hidden -Wait
    Start-Sleep -Milliseconds 500
    Start-Process -FilePath $taskExe -ArgumentList '--start' -WindowStyle Hidden
    $taskDeadline=[DateTime]::UtcNow.AddSeconds(35)
    do {
        Start-Sleep -Seconds 1
        $taskOnline=Read-ConnectionState
        if ($taskOnline.attached -and $taskOnline.lastQuotaUpdate -and !$taskOnline.quotaError) { break }
    } while ([DateTime]::UtcNow -lt $taskDeadline)
    $taskOnline | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $taskOutput 'online-recovered.json') -Encoding utf8
    if (!$taskOnline.attached -or !$taskOnline.lastQuotaUpdate -or $taskOnline.quotaError) { throw '正常连接未恢复' }
    Write-Output '正常连接和任务栏显示恢复：通过'
}
