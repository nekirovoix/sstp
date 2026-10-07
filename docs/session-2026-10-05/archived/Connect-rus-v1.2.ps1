$ErrorActionPreference = 'Stop'
$vpnName = 'rus'
$logFile = Join-Path $env:LOCALAPPDATA 'Connect-rus.log'

function Write-Log([string]$Message) {
    "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  $Message" | Add-Content -LiteralPath $logFile -Encoding UTF8
}

function Show-Failure([string]$Message) {
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
    Write-Log 'Started.'

    $vpn = Get-VpnConnection -Name $vpnName -ErrorAction Stop
    if ($vpn.ConnectionStatus -eq 'Connected') {
        Write-Log 'Already connected.'
        exit 0
    }

    # Never start another dial operation while Windows is still connecting or disconnecting.
    $deadline = (Get-Date).AddSeconds(30)
    while ($vpn.ConnectionStatus -in @('Connecting', 'Disconnecting') -and (Get-Date) -lt $deadline) {
        Write-Log "Waiting; current state: $($vpn.ConnectionStatus)."
        Start-Sleep -Milliseconds 750
        $vpn = Get-VpnConnection -Name $vpnName -ErrorAction Stop
        if ($vpn.ConnectionStatus -eq 'Connected') {
            Write-Log 'Connected while waiting.'
            exit 0
        }
    }

    if ($vpn.ConnectionStatus -in @('Connecting', 'Disconnecting')) {
        throw "VPN remains stuck in state '$($vpn.ConnectionStatus)'. Restart Windows once, then run this shortcut again."
    }

    # Use the exact Windows phonebook used by the current-user VPN profile.
    $candidatePhoneBooks = @(
        (Join-Path $env:APPDATA 'Microsoft\Network\Connections\Pbk\rasphone.pbk'),
        (Join-Path $env:ProgramData 'Microsoft\Network\Connections\Pbk\rasphone.pbk')
    )

    $phoneBook = $null
    foreach ($candidate in $candidatePhoneBooks) {
        if (Test-Path -LiteralPath $candidate) {
            $entryHeader = "[$vpnName]"
            if (Select-String -LiteralPath $candidate -SimpleMatch $entryHeader -Quiet) {
                $phoneBook = $candidate
                break
            }
        }
    }
    if (-not $phoneBook) {
        throw "The '$vpnName' entry was not found in the Windows VPN phonebook."
    }
    Write-Log "Phonebook selected: $phoneBook"

    if (-not ('NativeRasConnectorV12' -as [type])) {
        Add-Type @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

public static class NativeRasConnectorV12 {
    const int RASCM_UserName = 1;
    const int RASCM_Password = 2;
    const int RASCM_Domain = 4;

    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    struct RASCREDENTIALS {
        public int dwSize;
        public int dwMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=257)] public string szUserName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=257)] public string szPassword;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=16)] public string szDomain;
    }

    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    struct RASDIALPARAMS {
        public int dwSize;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=257)] public string szEntryName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=129)] public string szPhoneNumber;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=129)] public string szCallbackNumber;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=257)] public string szUserName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=257)] public string szPassword;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=16)] public string szDomain;
        public int dwSubEntry;
        public UIntPtr dwCallbackId;
        public int dwIfIndex;
    }

    [DllImport("rasapi32.dll", CharSet=CharSet.Unicode, EntryPoint="RasGetCredentialsW")]
    static extern uint RasGetCredentials(string phoneBook, string entryName, ref RASCREDENTIALS credentials);

    [DllImport("rasapi32.dll", CharSet=CharSet.Unicode, EntryPoint="RasDialW")]
    static extern uint RasDial(IntPtr extensions, string phoneBook, ref RASDIALPARAMS parameters,
                               uint notifierType, IntPtr notifier, out IntPtr connection);

    public static void Connect(string phoneBook, string entryName) {
        var credentials = new RASCREDENTIALS {
            dwSize = Marshal.SizeOf(typeof(RASCREDENTIALS)),
            dwMask = RASCM_UserName | RASCM_Password | RASCM_Domain,
            szUserName = "", szPassword = "", szDomain = ""
        };

        uint result = RasGetCredentials(phoneBook, entryName, ref credentials);
        if (result != 0)
            throw new Win32Exception((int)result, "RasGetCredentials failed with RAS code " + result);
        if ((credentials.dwMask & RASCM_Password) == 0)
            throw new Win32Exception(691, "Windows did not return a saved password for this VPN entry");

        var parameters = new RASDIALPARAMS {
            dwSize = Marshal.SizeOf(typeof(RASDIALPARAMS)),
            szEntryName = entryName,
            szPhoneNumber = "",
            szCallbackNumber = "",
            szUserName = credentials.szUserName,
            szPassword = credentials.szPassword,
            szDomain = credentials.szDomain,
            dwSubEntry = 0,
            dwCallbackId = UIntPtr.Zero,
            dwIfIndex = 0
        };

        IntPtr connection;
        try {
            result = RasDial(IntPtr.Zero, phoneBook, ref parameters, 0, IntPtr.Zero, out connection);
            if (result != 0)
                throw new Win32Exception((int)result, "RasDial failed with RAS code " + result);
        }
        finally {
            credentials.szPassword = "";
            parameters.szPassword = "";
        }
    }
}
'@
    }

    Write-Log 'Starting RasDial.'
    [NativeRasConnectorV12]::Connect($phoneBook, $vpnName)

    Start-Sleep -Seconds 1
    $final = Get-VpnConnection -Name $vpnName -ErrorAction Stop
    if ($final.ConnectionStatus -ne 'Connected') {
        throw "RasDial returned without an error, but VPN status is '$($final.ConnectionStatus)'."
    }

    Write-Log 'Connected successfully.'
    exit 0
}
catch {
    Show-Failure $_.Exception.Message
    exit 1
}
