$ErrorActionPreference = 'Stop'
$vpnName = 'rus'
$logFile = Join-Path $env:LOCALAPPDATA 'Connect-rus.log'

function Write-Log([string]$Message) {
    "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  $Message" | Add-Content -LiteralPath $logFile -Encoding UTF8
}

function Show-Error([string]$Message) {
    Write-Log "ERROR: $Message"
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show(
        "$Message`r`n`r`nLog: $logFile",
        'Connect rus',
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Error
    ) | Out-Null
}

try {
    Write-Log 'Launcher v1.3 started.'

    $vpn = Get-VpnConnection -Name $vpnName -ErrorAction Stop
    if ($vpn.ConnectionStatus -eq 'Connected') {
        Write-Log 'VPN is already connected.'
        exit 0
    }

    # rasphone uses the same native Windows dialog that successfully connects manually.
    $process = Start-Process -FilePath "$env:SystemRoot\System32\rasphone.exe" `
        -ArgumentList @('-d', $vpnName) -PassThru

    $shell = New-Object -ComObject WScript.Shell
    $activated = $false
    $activationDeadline = (Get-Date).AddSeconds(12)

    while ((Get-Date) -lt $activationDeadline) {
        Start-Sleep -Milliseconds 300
        try {
            if ($shell.AppActivate($process.Id)) {
                $activated = $true
                break
            }
        } catch { }
    }

    if (-not $activated) {
        throw 'The Windows VPN connection dialog did not become ready.'
    }

    # Connect is the default action in the rasphone dialog.
    Start-Sleep -Milliseconds 400
    $shell.SendKeys('{ENTER}')
    Write-Log 'Pressed the default Connect button.'

    $connectionDeadline = (Get-Date).AddSeconds(60)
    do {
        Start-Sleep -Milliseconds 750
        $vpn = Get-VpnConnection -Name $vpnName -ErrorAction Stop
        if ($vpn.ConnectionStatus -eq 'Connected') {
            Write-Log 'Connected successfully.'
            exit 0
        }
    } while ((Get-Date) -lt $connectionDeadline)

    throw "The VPN did not connect within 60 seconds. Final state: $($vpn.ConnectionStatus)."
}
catch {
    Show-Error $_.Exception.Message
    exit 1
}
