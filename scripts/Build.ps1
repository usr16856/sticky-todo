param([string]$Runtime = ('win-' + [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()))
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$publishExecutable = Join-Path $projectRoot "artifacts\publish\$Runtime\StickyTodo.exe"
if (Get-Process -Name StickyTodo -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $publishExecutable }) {
    throw '發佈目錄中的 App 仍在執行，請先結束測試 App 再建置。'
}
$localDotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
$dotnetCommand = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
& $dotnetCommand run --project (Join-Path $projectRoot 'tests\StickyTodo.Tests\StickyTodo.Tests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw '資料層測試失敗，停止發佈。' }
& $dotnetCommand run --project (Join-Path $projectRoot 'tests\StickyTodo.UiTests\StickyTodo.UiTests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'WPF 表單驗證失敗，停止發佈。' }
& $dotnetCommand publish (Join-Path $projectRoot 'src\StickyTodo.App\StickyTodo.App.csproj') -c Release -r $Runtime --self-contained true -o (Join-Path $projectRoot "artifacts\publish\$Runtime")
if ($LASTEXITCODE -ne 0) { throw '建置失敗。' }
Write-Output "發佈完成：$projectRoot\artifacts\publish\$Runtime"
