param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string] $Command
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$packetLabFile = Join-Path $root "src\bin\Debug\packet-lab.txt"

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $packetLabFile) | Out-Null
Set-Content -LiteralPath $packetLabFile -Value $Command -Encoding UTF8

Write-Host "Sent packet lab command: $Command"
