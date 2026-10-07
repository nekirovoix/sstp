# SSTP Deploy Manager

Windows desktop utility for deploying a SoftEther SSTP server on Ubuntu over SSH and creating a native Windows SSTP profile.

## Features

- Persian RTL WinForms interface
- SSH deployment with host-key confirmation
- SoftEther VPN Server installation and repair
- Let's Encrypt certificate setup and renewal hook
- Native Windows SSTP profile creation
- Full-tunnel routes for IPv4 and IPv6
- Secure credential persistence through the Windows RAS API
- Detection of TCP 443 conflicts such as Xray, Nginx, or Apache
- Passwords are not stored in the application's settings or logs

## Repository layout

- `src/SstpDeployManager/` — .NET 8 WinForms source
- `scripts/deploy-sstp.sh` — readable reference copy of the embedded Ubuntu deployment script
- `docs/README-fa.md` — detailed Persian documentation and version history
- `.github/workflows/build.yml` — Windows x64 self-contained build

## Requirements

### Windows client

- Windows 10 or Windows 11 x64
- Administrator approval through UAC

### Ubuntu server

- Root SSH access, or passwordless sudo
- A DNS A record pointing the VPN hostname to the server
- TCP ports 80 and 443 available
- Port 443 must not be owned by another service

## Build

```powershell
dotnet restore src/SstpDeployManager/SstpDeployManager.csproj
dotnet publish src/SstpDeployManager/SstpDeployManager.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true
```

## Security notes

- Never commit server passwords, VPN passwords, private keys, generated configuration, or `/root/sstp-credentials.txt`.
- Verify the SSH host-key fingerprint on first connection.
- SSTP must own TCP 443. If Xray or another TLS service uses 443, stop or move it first.
- The executable is not code-signed; production distributions should be signed.

## License

No license has been selected yet. Add a license before accepting external contributions or redistribution.

## Keyboard quick connect

`tools/SstpQuickConnect` contains a separate, non-resident helper. Running it connects the saved VPN profile and exits. On first run it creates a Start Menu shortcut with the `Ctrl+Alt+V` hotkey. It does not use Startup or Task Scheduler and stores no password.

## SSTP launcher and kill-switch session archive

[Persian session documentation and source files](docs/session-2026-10-05/README-fa.md) include the tested rasphone launcher, firewall helper, historical versions, recovery commands, and reported test evidence. The firewall helper is experimental and does **not** guarantee leak-free operation; review its security limitations before use. Existing application sources are unchanged.
