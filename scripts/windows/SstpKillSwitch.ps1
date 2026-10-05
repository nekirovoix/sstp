param(
    [ValidateSet('Enable', 'Disable', 'Status')]
    [string]$Action = ''
)

$ErrorActionPreference = 'Stop'
$Group = 'SSTP Kill Switch'
$Domain = 't.navar-abyari.ir'
$StatePath = Join-Path $env:ProgramData 'SstpKillSwitch\firewall-state.json'

if (-not $Action) {
    Write-Host '1) Enable Kill Switch'
    Write-Host '2) Disable Kill Switch and restore internet'
    Write-Host '3) Show status'
    $choice = Read-Host 'Select 1, 2, or 3'
    $Action = switch ($choice) { '1' { 'Enable' } '2' { 'Disable' } '3' { 'Status' } default { throw 'Invalid selection.' } }
}

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

if ($Action -ne 'Status' -and -not $isAdmin) {
    $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Action $Action"
    Start-Process powershell.exe -Verb RunAs -ArgumentList $arguments
    exit
}

if ($Action -eq 'Status') {
    $profiles = Get-NetFirewallProfile | Select-Object Name, DefaultOutboundAction
    $rules = Get-NetFirewallRule -Group $Group -ErrorAction SilentlyContinue
    $profiles | Format-Table -AutoSize
    if ($rules) { Write-Host 'SSTP Kill Switch rules: ENABLED' -ForegroundColor Green }
    else { Write-Host 'SSTP Kill Switch rules: DISABLED' -ForegroundColor Yellow }
    exit
}

if ($Action -eq 'Enable') {
    $serverIp = Resolve-DnsName $Domain -Type A -ErrorAction Stop |
        Where-Object IPAddress | Select-Object -ExpandProperty IPAddress -First 1
    if (-not $serverIp) { throw "Could not resolve $Domain to an IPv4 address." }

    $stateDir = Split-Path $StatePath
    New-Item -ItemType Directory -Path $stateDir -Force | Out-Null
    if (-not (Test-Path $StatePath)) {
        $saved = @{}
        Get-NetFirewallProfile | ForEach-Object { $saved[$_.Name] = $_.DefaultOutboundAction.ToString() }
        $saved | ConvertTo-Json | Set-Content -Encoding UTF8 $StatePath
    }

    Get-NetFirewallRule -Group $Group -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    New-NetFirewallRule -DisplayName 'SSTP Allow VPN tunnel traffic' -Group $Group -Direction Outbound -Action Allow -InterfaceType RemoteAccess -Profile Any | Out-Null
    New-NetFirewallRule -DisplayName 'SSTP Allow server TCP 443' -Group $Group -Direction Outbound -Action Allow -Protocol TCP -RemotePort 443 -RemoteAddress $serverIp -Profile Any | Out-Null

    $dns = Get-DnsClientServerAddress | ForEach-Object ServerAddresses |
        Where-Object { $_ -and $_ -notmatch '^127\.' -and $_ -ne '::1' } | Sort-Object -Unique
    if ($dns) {
        New-NetFirewallRule -DisplayName 'SSTP Allow DNS UDP' -Group $Group -Direction Outbound -Action Allow -Protocol UDP -RemotePort 53 -RemoteAddress $dns -Profile Any | Out-Null
        New-NetFirewallRule -DisplayName 'SSTP Allow DNS TCP' -Group $Group -Direction Outbound -Action Allow -Protocol TCP -RemotePort 53 -RemoteAddress $dns -Profile Any | Out-Null
    }

    New-NetFirewallRule -DisplayName 'SSTP Allow DHCPv4' -Group $Group -Direction Outbound -Action Allow -Protocol UDP -LocalPort 68 -RemotePort 67 -Profile Any | Out-Null
    New-NetFirewallRule -DisplayName 'SSTP Allow DHCPv6' -Group $Group -Direction Outbound -Action Allow -Protocol UDP -LocalPort 546 -RemotePort 547 -Profile Any | Out-Null
    Set-NetFirewallProfile -Name Domain, Private, Public -DefaultOutboundAction Block
    Write-Host "Kill Switch enabled. SSTP server allowed at ${serverIp}:443." -ForegroundColor Green
    exit
}

Get-NetFirewallRule -Group $Group -ErrorAction SilentlyContinue | Remove-NetFirewallRule
if (Test-Path $StatePath) {
    $saved = Get-Content $StatePath -Raw | ConvertFrom-Json
    $saved.PSObject.Properties | ForEach-Object {
        Set-NetFirewallProfile -Name $_.Name -DefaultOutboundAction $_.Value
    }
    Remove-Item $StatePath -Force
} else {
    Set-NetFirewallProfile -Name Domain, Private, Public -DefaultOutboundAction Allow
}
Write-Host 'Kill Switch disabled and previous outbound firewall policy restored.' -ForegroundColor Green
