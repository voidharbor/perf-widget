# Builds PerfWidget.exe with the .NET Framework compiler that ships with Windows, runs the tests,
# installs the exe under %LOCALAPPDATA%\Programs\PerfWidget, adds the Startup shortcut and launches it.
# From PowerShell:  .\build.ps1        From WSL:  powershell.exe -NoProfile -ExecutionPolicy Bypass -File '\\wsl.localhost\<distro>\<path>\perf-widget\build.ps1'
param([switch]$NoLaunch, [switch]$NoStartup, [switch]$TestsOnly)
$ErrorActionPreference = 'Stop'

$Src     = Join-Path $PSScriptRoot 'src'
$Tests   = Join-Path $PSScriptRoot 'tests'
$Csc     = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$Stage   = Join-Path $env:LOCALAPPDATA 'PerfWidget\build'
$Install = Join-Path $env:LOCALAPPDATA 'Programs\PerfWidget'
$Exe     = Join-Path $Install 'PerfWidget.exe'

New-Item -ItemType Directory -Force $Stage, $Install | Out-Null
Remove-Item "$Stage\*" -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item "$Src\*" $Stage
Copy-Item "$Tests\*.cs" $Stage

Write-Host '== tests'
& $Csc /nologo /target:exe /out:"$Stage\SeverityTests.exe" "$Stage\Severity.cs" "$Stage\SeverityTests.cs"
if ($LASTEXITCODE) { throw 'test compile failed' }
& "$Stage\SeverityTests.exe"
if ($LASTEXITCODE) { throw 'tests failed' }
if ($TestsOnly) { exit 0 }

Write-Host '== build'
$refs = 'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'Microsoft.CSharp.dll' | ForEach-Object { "/r:$_" }
$srcs = Get-ChildItem "$Stage\*.cs" | Where-Object { $_.Name -ne 'SeverityTests.cs' } | ForEach-Object { $_.FullName }
& $Csc /nologo /target:winexe /optimize+ /warn:3 /win32manifest:"$Stage\app.manifest" /out:"$Stage\PerfWidget.exe" @refs @srcs
if ($LASTEXITCODE) { throw 'build failed' }

Write-Host '== install'
if (Test-Path $Exe) {
    & $Exe --quit | Out-Null
    $deadline = (Get-Date).AddSeconds(5)
    while ((Get-Process -Name PerfWidget -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 200 }
    Get-Process -Name PerfWidget -ErrorAction SilentlyContinue | Stop-Process -Force
}
Copy-Item "$Stage\PerfWidget.exe" $Exe -Force
Write-Host "installed $Exe"

if (-not $NoStartup) { & $Exe --startup on; Write-Host "startup shortcut: $([Environment]::GetFolderPath('Startup'))\PerfWidget.lnk" }
if (-not $NoLaunch) { Start-Process $Exe; Start-Sleep -Seconds 2; Write-Host "running: $((Get-Process -Name PerfWidget -ErrorAction SilentlyContinue | Measure-Object).Count) process(es)" }
