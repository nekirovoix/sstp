# Changelog

## 1.11.0
- Persist VPN credentials with the Windows `RasSetCredentials` API.
- Update saved credentials before manual connection.

## 1.10.0
- Require Administrator privileges through the application manifest.
- Convert PowerShell failures to readable text.

## 1.9.0
- Replace unsupported default VPN routes with IPv4 and IPv6 `/1` route pairs.

## 1.8.0
- Enforce full-tunnel routing and verify active VPN routes.
- Detect non-SoftEther services occupying TCP 443.

## 1.7.0
- Use `cert.pem` for SoftEther and install the intermediate chain separately.
- Stop on Certbot failures.

## 1.6.0
- Add strict certificate fingerprint and chain verification.

## 1.1.0–1.5.0
- Add generated VPN passwords, recovery of partial installations, localhost management fallback, firewall restrictions, certificate renewal, and SSTP certificate diagnostics.

## Utilities 1.0.0
- Add `Connect-rus.exe` source for one-shot connection of the saved `rus` profile.
- Add persistent firewall-based SSTP Kill Switch manager with restore support.
- Add equivalent PowerShell scripts for connection and Kill Switch management.

## Kill Switch 1.1.0
- Detect the currently connected SSTP profile dynamically.
- Read its profile name, server address, and IPv4 endpoint before applying firewall policy.
- Refuse to enable when no SSTP profile is connected.
- Preserve compatibility with state files created by version 1.0.

## Connector 1.1.0
- Replace `rasdial.exe` with direct Windows RAS API calls.
- Retrieve the saved credential handle through `RasGetCredentials` and connect with `RasDial`.
- Avoid duplicate connection references when the `rus` profile is already connected.
