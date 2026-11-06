$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.tools\cli-home'
$env:NUGET_PACKAGES = if ($env:CODEX_TASKBAR_NUGET) { $env:CODEX_TASKBAR_NUGET } else { Join-Path $taskRoot '.tools\nuget' }
$env:APPDATA = Join-Path $taskRoot '.tools\appdata'
$env:LOCALAPPDATA = Join-Path $taskRoot '.tools\localappdata'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$taskDotnet = Join-Path $taskRoot '.tools\dotnet\dotnet.exe'
if ($env:CODEX_TASKBAR_DOTNET) { $taskDotnet = $env:CODEX_TASKBAR_DOTNET }
if (!(Test-Path -LiteralPath $taskDotnet)) { $taskDotnet = (Get-Command dotnet -ErrorAction Stop).Source }
& $taskDotnet @args
exit $LASTEXITCODE
