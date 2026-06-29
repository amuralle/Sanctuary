$ErrorActionPreference = "Continue"

$root = Split-Path -Parent $PSScriptRoot
$localData = Join-Path $root "src\.local_data"
$pidFile = Join-Path $localData "sanctuary-service-pids.txt"
$serviceDlls = @(
    "Sanctuary.WebAPI.dll",
    "Sanctuary.Login.dll",
    "Sanctuary.Gateway.dll"
)

$pids = @()

if (Test-Path -LiteralPath $pidFile) {
    $records = Get-Content -Raw -LiteralPath $pidFile | ConvertFrom-Json
    $pids = @($records | ForEach-Object { $_.Id })
}

foreach ($id in $pids) {
    $process = Get-Process -Id $id -ErrorAction SilentlyContinue
    if ($process) {
        Stop-Process -Id $id -Force
        Write-Host "Stopped PID $id"
    }
}

Get-CimInstance Win32_Process -Filter "name = 'dotnet.exe'" |
    Where-Object {
        $commandLine = $_.CommandLine
        $serviceDlls | Where-Object { $commandLine -like "*$_*" }
    } |
    ForEach-Object {
        Stop-Process -Id $_.ProcessId -Force
        Write-Host "Stopped PID $($_.ProcessId)"
    }

Remove-Item -LiteralPath $pidFile -Force -ErrorAction SilentlyContinue
