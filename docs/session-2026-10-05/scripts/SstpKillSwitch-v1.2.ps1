param(
    [ValidateSet('Enable', 'Disable', 'Status', 'EmergencyReset')]
    [string]$Action = ''
)

$ErrorActionPreference = 'Stop'
$Group = 'SSTP Kill Switch'
$StatePath = Join-Path $env:ProgramData 'SstpKillSwitch\firewall-state.json'

if (-not $Action) {
    Write-Host '1) Enable Kill Switch'
    Write-Host '2) Disable Kill Switch and restore previous firewall policy'
    Write-Host '3) Show status'
    Write-Host '4) Emergency reset (remove rules and force outbound Allow)'
    $choice = Read-Host 'Select 1, 2, 3, or 4'
    $Action = switch ($choice) {
        '1' { 'Enable' }
        '2' { 'Disable' }
        '3' { 'Status' }
        '4' { 'EmergencyReset' }
        default { throw 'Invalid selection.' }
    }
}

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

if ($Action -ne 'Status' -and -not $isAdmin) {
    $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Action $Action"
    Start-Process powershell.exe -Verb RunAs -ArgumentList $arguments
    exit
}

function Get-SavedState {
    if (-not (Test-Path -LiteralPath $StatePath)) { return $null }
    return (Get-Content -LiteralPath $StatePath -Raw | ConvertFrom-Json)
}

function Restore-SavedPolicy {
    param([Parameter(Mandatory)]$State)
    foreach ($item in @($State.FirewallProfiles)) {
        Set-NetFirewallProfile -Name $item.Name -DefaultOutboundAction $item.DefaultOutboundAction
    }
}

if ($Action -eq 'Status') {
    $profiles = Get-NetFirewallProfile | Select-Object Name, Enabled, DefaultOutboundAction
    $rules = @(Get-NetFirewallRule -Group $Group -ErrorAction SilentlyContinue)
    $state = Get-SavedState
    $profiles | Format-Table -AutoSize
    if ($rules.Count -gt 0) {
        Write-Host "SSTP Kill Switch rules: PRESENT ($($rules.Count))" -ForegroundColor Green
    } else {
        Write-Host 'SSTP Kill Switch rules: ABSENT' -ForegroundColor Yellow
    }
    if ($state) {
        Write-Host "Saved profile: $($state.VpnName) | Server: $($state.ServerAddress) | IPs: $(@($state.ServerIps) -join ', ')"
    } else {
        Write-Host 'Saved restore state: ABSENT' -ForegroundColor Yellow
    }
    exit
}

if ($Action -eq 'EmergencyReset') {
    Get-NetFirewallRule -Group $Group -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    Set-NetFirewallProfile -Name Domain, Private, Public -DefaultOutboundAction Allow
    if (Test-Path -LiteralPath $StatePath) { Remove-Item -LiteralPath $StatePath -Force }
    Write-Host 'Emergency reset completed: Kill Switch rules removed and outbound policy forced to Allow.' -ForegroundColor Yellow
    exit
}

if ($Action -eq 'Disable') {
    $state = Get-SavedState
    Get-NetFirewallRule -Group $Group -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    if ($state) {
        Restore-SavedPolicy -State $state
        Remove-Item -LiteralPath $StatePath -Force
        Write-Host 'Kill Switch disabled and the previous outbound firewall policy restored.' -ForegroundColor Green
    } else {
        Write-Host 'Kill Switch rules removed. No saved state existed, so firewall profile defaults were not changed.' -ForegroundColor Yellow
        Write-Host 'Use -Action EmergencyReset only if outbound traffic remains blocked.' -ForegroundColor Yellow
    }
    exit
}

# Enable
$vpns = @()
$vpns += @(Get-VpnConnection -ErrorAction SilentlyContinue)
$vpns += @(Get-VpnConnection -AllUserConnection -ErrorAction SilentlyContinue)
$vpn = $vpns | Where-Object {
    $_.ConnectionStatus -eq 'Connected' -and $_.TunnelType -eq 'Sstp'
} | Select-Object -First 1
if (-not $vpn) { throw 'No connected SSTP profile was found. Connect the desired SSTP profile first.' }

$server = $vpn.ServerAddress
$serverIps = @([System.Net.Dns]::GetHostAddresses($server) |
    Where-Object { $_.AddressFamily -eq 'InterNetwork' } |
    ForEach-Object { $_.ToString() } |
    Sort-Object -Unique)
if ($serverIps.Count -eq 0) { throw "Could not resolve SSTP server '$server' to IPv4." }

$stateDir = Split-Path -Parent $StatePath
New-Item -ItemType Directory -Path $stateDir -Force | Out-Null
$existingState = Get-SavedState
if ($existingState) {
    $savedProfiles = @($existingState.FirewallProfiles)
} else {
    $savedProfiles = @(Get-NetFirewallProfile | ForEach-Object {
        [ordered]@{
            Name = $_.Name
            DefaultOutboundAction = $_.DefaultOutboundAction.ToString()
        }
    })
}

$state = [ordered]@{
    Version = '1.2'
    SavedAt = (Get-Date).ToString('o')
    FirewallProfiles = $savedProfiles
    VpnName = $vpn.Name
    ServerAddress = $server
    ServerIps = $serverIps
}
$state | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $StatePath -Encoding UTF8

try {
    Get-NetFirewallRule -Group $Group -ErrorAction SilentlyContinue | Remove-NetFirewallRule

    New-NetFirewallRule -DisplayName 'SSTP Allow VPN tunnel traffic' -Group $Group `
        -Direction Outbound -Action Allow -InterfaceType RemoteAccess -Profile Any | Out-Null

    New-NetFirewallRule -DisplayName 'SSTP Allow server TCP 443' -Group $Group `
        -Direction Outbound -Action Allow -Protocol TCP -RemotePort 443 `
        -RemoteAddress $serverIps -Profile Any | Out-Null

    $dns = @(Get-DnsClientServerAddress | ForEach-Object ServerAddresses |
        Where-Object { $_ -and $_ -notmatch '^127\.' -and $_ -ne '::1' } |
        Sort-Object -Unique)
    if ($dns.Count -gt 0) {
        New-NetFirewallRule -DisplayName 'SSTP Allow DNS UDP' -Group $Group `
            -Direction Outbound -Action Allow -Protocol UDP -RemotePort 53 `
            -RemoteAddress $dns -Profile Any | Out-Null
        New-NetFirewallRule -DisplayName 'SSTP Allow DNS TCP' -Group $Group `
            -Direction Outbound -Action Allow -Protocol TCP -RemotePort 53 `
            -RemoteAddress $dns -Profile Any | Out-Null
    }

    New-NetFirewallRule -DisplayName 'SSTP Allow DHCPv4' -Group $Group `
        -Direction Outbound -Action Allow -Protocol UDP -LocalPort 68 -RemotePort 67 -Profile Any | Out-Null
    New-NetFirewallRule -DisplayName 'SSTP Allow DHCPv6' -Group $Group `
        -Direction Outbound -Action Allow -Protocol UDP -LocalPort 546 -RemotePort 547 -Profile Any | Out-Null

    Set-NetFirewallProfile -Name Domain, Private, Public -DefaultOutboundAction Block
    Write-Host "Kill Switch enabled for SSTP profile '$($vpn.Name)' at $($serverIps -join ', '):443." -ForegroundColor Green
}
catch {
    Get-NetFirewallRule -Group $Group -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    Restore-SavedPolicy -State $state
    if (Test-Path -LiteralPath $StatePath) { Remove-Item -LiteralPath $StatePath -Force }
    throw "Enable failed and firewall policy was rolled back. $($_.Exception.Message)"
}
