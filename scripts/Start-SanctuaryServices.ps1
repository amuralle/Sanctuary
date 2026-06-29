$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"

if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnet = "dotnet"
}

$sanctuarySrc = Join-Path $root "src"
$bin = Join-Path $sanctuarySrc "bin\Debug"
$localData = Join-Path $sanctuarySrc ".local_data"
$logDir = Join-Path $localData "logs"
$pidFile = Join-Path $localData "sanctuary-service-pids.txt"

New-Item -ItemType Directory -Force -Path $logDir | Out-Null

& $dotnet build (Join-Path $sanctuarySrc "Sanctuary.sln")

if ($LASTEXITCODE -ne 0) {
    throw "Build failed."
}

$env:DOTNET_ENVIRONMENT = "Development"
$env:ASPNETCORE_ENVIRONMENT = "Development"

$services = @(
    @{ Name = "WebAPI"; Dll = "Sanctuary.WebAPI.dll"; Delay = 2 },
    @{ Name = "Login"; Dll = "Sanctuary.Login.dll"; Delay = 2 },
    @{ Name = "Gateway"; Dll = "Sanctuary.Gateway.dll"; Delay = 0 }
)

$started = @()

foreach ($service in $services) {
    $stdout = Join-Path $logDir "$($service.Name).out.log"
    $stderr = Join-Path $logDir "$($service.Name).err.log"

    $process = Start-Process `
        -FilePath $dotnet `
        -ArgumentList $service.Dll `
        -WorkingDirectory $bin `
        -WindowStyle Hidden `
        -RedirectStandardOutput $stdout `
        -RedirectStandardError $stderr `
        -PassThru

    $started += [pscustomobject]@{
        Name = $service.Name
        Id = $process.Id
        Dll = $service.Dll
    }

    if ($service.Delay -gt 0) {
        Start-Sleep -Seconds $service.Delay
    }
}

$started | ConvertTo-Json | Set-Content -LiteralPath $pidFile

Write-Host "Started Sanctuary services:"
$started | Format-Table -AutoSize
Write-Host "Logs: $logDir"
Write-Host "Stop with: scripts\Stop-SanctuaryServices.ps1"
