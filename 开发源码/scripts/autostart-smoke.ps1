param(
    [string]$Executable=(Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) '运行程序\Codex用量任务栏.exe'),
    [string]$Shortcut=(Join-Path ([Environment]::GetFolderPath('Startup')) 'Codex用量任务栏.lnk')
)
$ErrorActionPreference='Stop'
$taskExe=(Resolve-Path -LiteralPath $Executable).Path
$taskProcessNames=@('CodexTaskbar',[IO.Path]::GetFileNameWithoutExtension($taskExe))|Sort-Object -Unique
$taskBefore=@(Get-Process -Name $taskProcessNames -ErrorAction SilentlyContinue)
if($taskBefore.Count -gt 1){throw '存在多个组件实例，停止测试'}
$taskRestoreExe=if($taskBefore.Count -eq 1){$taskBefore[0].Path}else{$taskExe}
$taskKey=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run',$true)
$taskOriginal=$taskKey.GetValue('CodexTaskbar');$taskKind=$taskKey.GetValueKind('CodexTaskbar')
$taskSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$taskHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($taskSid))).Substring(0,16)
$taskPipeName='CodexTaskbar-'+$taskHash+'-'+[Diagnostics.Process]::GetCurrentProcess().SessionId
$taskLaunched=@()
function Invoke-Component([string]$taskCommand){
    $taskPipe=[IO.Pipes.NamedPipeClientStream]::new('.',$taskPipeName,[IO.Pipes.PipeDirection]::InOut,[IO.Pipes.PipeOptions]::Asynchronous)
    try{
        try{$taskPipe.Connect(300)}catch [TimeoutException]{return $null}
        $taskWriter=[IO.StreamWriter]::new($taskPipe,[Text.UTF8Encoding]::new($false),4096,$true);$taskWriter.AutoFlush=$true
        $taskReader=[IO.StreamReader]::new($taskPipe,[Text.Encoding]::UTF8,$false,4096,$true)
        try{$taskWriter.WriteLine((@{Command=$taskCommand}|ConvertTo-Json -Compress));return ($taskReader.ReadLineAsync().WaitAsync([TimeSpan]::FromSeconds(5)).GetAwaiter().GetResult()|ConvertFrom-Json)}
        finally{$taskReader.Dispose();$taskWriter.Dispose()}
    }finally{$taskPipe.Dispose()}
}
function Stop-Component{
    $null=Invoke-Component 'stop'
    $taskDeadline=[DateTime]::UtcNow.AddSeconds(5)
    while((Get-Process -Name $taskProcessNames -ErrorAction SilentlyContinue) -and [DateTime]::UtcNow -lt $taskDeadline){Start-Sleep -Milliseconds 100}
    if(Get-Process -Name $taskProcessNames -ErrorAction SilentlyContinue){throw '组件未正常退出'}
}
try{
    Stop-Component
    $taskKey.DeleteValue('CodexTaskbar',$false)
    $taskOff=Start-Process -FilePath $taskExe -ArgumentList '--autostart' -WindowStyle Hidden -PassThru
    $taskLaunched+=,$taskOff
    if(!$taskOff.WaitForExit(5000) -or $taskOff.ExitCode -ne 0){throw '关闭开机启动后，自动启动入口仍在运行'}
    if(Get-Process -Name $taskProcessNames -ErrorAction SilentlyContinue){throw '关闭开关后仍拉起组件'}
    Write-Output 'PASS disabled autostart exits without launching the component'
    $taskKey.SetValue('CodexTaskbar',('"'+$taskExe+'" --start'),[Microsoft.Win32.RegistryValueKind]::String)
    $taskPending=Start-Process -FilePath $taskExe -ArgumentList '--autostart' -WindowStyle Hidden -PassThru
    $taskLaunched+=,$taskPending
    Start-Sleep -Seconds 2
    $taskKey.DeleteValue('CodexTaskbar',$false)
    if(!$taskPending.WaitForExit(20000) -or $taskPending.ExitCode -ne 0 -or $null -ne (Invoke-Component 'status')){throw '延迟期间关闭开关后仍启动了组件'}
    Write-Output 'PASS disabling during startup delay cancels the pending launch'
    if(!(Test-Path -LiteralPath $Shortcut)){throw '缺少原生启动快捷方式'}
    $taskShell=New-Object -ComObject WScript.Shell
    try{
        $taskLink=$taskShell.CreateShortcut($Shortcut)
        if($taskLink.TargetPath -ne $taskExe -or $taskLink.Arguments -ne '--autostart'){throw '快捷方式必须直接指向组件的--autostart入口'}
    }finally{if($null -ne $taskLink){[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($taskLink)};[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($taskShell)}
    $taskKey.SetValue('CodexTaskbar',('"'+$taskExe+'" --start'),[Microsoft.Win32.RegistryValueKind]::String)
    $taskWatch=[Diagnostics.Stopwatch]::StartNew()
    $taskOn=Start-Process -FilePath $Shortcut -WindowStyle Hidden -PassThru
    $taskLaunched+=,$taskOn
    Start-Sleep -Seconds 5
    if($null -ne (Invoke-Component 'status')){throw '登录启动未等待桌面初始化'}
    $taskDeadline=[DateTime]::UtcNow.AddSeconds(45)
    do{
        $taskState=Invoke-Component 'status'
        if($taskState.running -and $taskState.attached -and $taskState.visible){break}
        Start-Sleep -Milliseconds 300
    }while([DateTime]::UtcNow -lt $taskDeadline)
    if(!$taskState.running -or !$taskState.attached -or !$taskState.visible -or $taskWatch.Elapsed.TotalSeconds -lt 14){throw '登录启动未成功挂接任务栏'}
    Write-Output 'PASS direct native shortcut waits for desktop and launches an attached component'
    [pscustomobject]@{version=$taskState.version;pid=$taskState.pid;attached=$taskState.attached;visible=$taskState.visible;elapsedSeconds=[math]::Round($taskWatch.Elapsed.TotalSeconds,1)}|ConvertTo-Json
    $taskDuplicate=Start-Process -FilePath $taskExe -ArgumentList '--autostart' -WindowStyle Hidden -PassThru
    $taskLaunched+=,$taskDuplicate
    if(!$taskDuplicate.WaitForExit(25000) -or $taskDuplicate.ExitCode -ne 0){throw '重复自动启动未正常退出'}
    $taskDuplicateState=Invoke-Component 'status'
    if($taskDuplicateState.pid -ne $taskState.pid -or $taskDuplicateState.menuVisible){throw '重复自动启动创建了新实例或打开了菜单'}
    Write-Output 'PASS duplicate autostart refreshes the instance without opening a menu'
}finally{
    $null=Invoke-Component 'stop'
    foreach($taskChild in $taskLaunched){if($null -ne $taskChild -and !$taskChild.HasExited -and !$taskChild.WaitForExit(5000)){$taskChild.Kill();$taskChild.WaitForExit()};if($null -ne $taskChild){$taskChild.Dispose()}}
    $taskKey.SetValue('CodexTaskbar',$taskOriginal,$taskKind);$taskKey.Dispose()
    if(!(Get-Process -Name $taskProcessNames -ErrorAction SilentlyContinue)){Start-Process -FilePath $taskRestoreExe -ArgumentList '--start' -WindowStyle Hidden}
}
