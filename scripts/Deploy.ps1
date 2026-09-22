param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$runtimeName = 'win-' + [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'Build.ps1') -Runtime $runtimeName }
$publishDirectory = Join-Path $projectRoot "artifacts\publish\$runtimeName"
$installDirectory = Join-Path $env:LOCALAPPDATA 'Programs\StickyTodo'
$executablePath = Join-Path $installDirectory 'StickyTodo.exe'
if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory 'StickyTodo.exe'))) { throw '找不到發佈檔案，請先執行 Build.ps1。' }
$runningApp = Get-Process -Name StickyTodo -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $executablePath }
if ($runningApp) { throw '請先從 StickyTodo 設定或系統匣選擇「結束 StickyTodo」，再重新部署。' }
# Copy only published binaries. No application data or settings are touched.
New-Item -ItemType Directory -Force -Path $installDirectory | Out-Null
Get-ChildItem -LiteralPath $publishDirectory | Copy-Item -Destination $installDirectory -Recurse -Force
$shortcutShell = New-Object -ComObject WScript.Shell
$shortcutDirectories = @([Environment]::GetFolderPath('Desktop'), (Join-Path ([Environment]::GetFolderPath('Programs')) 'StickyTodo'))
foreach ($shortcutDirectory in $shortcutDirectories) {
    New-Item -ItemType Directory -Force -Path $shortcutDirectory | Out-Null
    $shortcut = $shortcutShell.CreateShortcut((Join-Path $shortcutDirectory 'StickyTodo.lnk'))
    $shortcut.TargetPath = $executablePath
    $shortcut.IconLocation = "$executablePath,0"
    $shortcut.WorkingDirectory = $installDirectory
    $shortcut.Description = 'StickyTodo 我的待辦便條紙'
    $shortcut.Save()
}
Write-Output "已部署：$executablePath"
