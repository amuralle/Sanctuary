# Local Development

This branch contains the current local Sanctuary development rig used for collection-node testing. It is not a finished gameplay implementation. It is intended to make it easy for another developer to run the server, connect a client, place collection nodes, and test packet behavior.

## Repositories

Use these two repositories as siblings:

```powershell
git clone https://github.com/amuralle/Sanctuary.git
git clone https://github.com/amuralle/Launcher.git
```

The examples below assume:

```text
C:\Users\<you>\Documents\Projects\Sanctuary
C:\Users\<you>\Documents\Projects\Launcher
```

## Requirements

- Windows
- Visual Studio 2022 or the .NET SDK
- .NET 9 SDK or newer
- OSFR Launcher built from the sibling `Launcher` repo
- A Free Realms client installed by the launcher

The repo targets `net9.0`. A newer SDK can build it as long as the .NET 9 targeting pack is present.

## Build And Start Sanctuary

From the Sanctuary repo root:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Start-SanctuaryServices.ps1
```

That script builds the solution and starts:

- `Sanctuary.WebAPI.dll` on `http://127.0.0.1:20040`
- `Sanctuary.Login.dll` on `127.0.0.1:20042`
- `Sanctuary.Gateway.dll` on `127.0.0.1:20260`

Logs and the service PID file are written under:

```text
src\.local_data\
```

That directory is intentionally git-ignored.

Stop the local services with:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Stop-SanctuaryServices.ps1
```

## Launch The Launcher

Build the Launcher repo, then run its debug build:

```powershell
$dotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
Start-Process -FilePath $dotnet -ArgumentList "Launcher.dll" -WorkingDirectory "..\Launcher\src\Launcher\bin\Debug\net10.0"
```

The local Sanctuary WebAPI exposes a launcher manifest with:

```text
Name: Local Sanctuary
Web API: http://127.0.0.1:20040
Login: 127.0.0.1:20042
Client files path: Client
```

If the launcher asks for a server URL, point it at:

```text
http://127.0.0.1:20040
```

## Useful Logs

The most useful log during gameplay is:

```text
src\.local_data\logs\Gateway.out.log
```

Watch the tail while testing:

```powershell
Get-Content .\src\.local_data\logs\Gateway.out.log -Tail 80 -Wait
```

If the client disconnects, the Gateway log usually shows the last packet before the disconnect.

## Packet Lab

`DevPacketLabService` watches:

```text
src\bin\Debug\packet-lab.txt
```

Send a command to the currently logged-in player with:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Send-PacketLabCommand.ps1 "collect list"
```

This lets us test packets and spawn nodes without opening in-game chat.

## Current Local Flow

1. Start Sanctuary services.
2. Launch the launcher.
3. Hit Play.
4. Log in.
5. Use packet-lab commands from PowerShell while watching the client.
6. Stop services when done.

If the Gateway process is already running, rebuilds may fail because `Sanctuary.Gateway.dll` is locked. Run the stop script first.

## What Is Experimental

The collection-node work is a prototype. Inventory rewards, node interaction, respawn, and login-time collection rows work well enough for testing. Live updates to the already-open Collections panel and the exact original collection notification packet are still under investigation.
