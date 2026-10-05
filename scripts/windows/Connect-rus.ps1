$ErrorActionPreference = 'Stop'
$profile = 'rus'

$current = Get-VpnConnection -Name $profile -ErrorAction Stop
if ($current.ConnectionStatus -eq 'Connected') { exit 0 }

if (-not ('NativeRasConnector' -as [type])) {
Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class NativeRasConnector {
  const int UserName = 1, Password = 2, Domain = 4;

  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
  struct Credentials {
    public int Size, Mask;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst=257)] public string User;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst=257)] public string Pass;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst=16)] public string DomainName;
  }

  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
  struct DialParams {
    public int Size;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst=257)] public string Entry;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst=129)] public string Phone;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst=129)] public string Callback;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst=257)] public string User;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst=257)] public string Pass;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst=16)] public string DomainName;
    public int SubEntry;
    public UIntPtr CallbackId;
    public int InterfaceIndex;
  }

  [DllImport("rasapi32.dll", CharSet=CharSet.Unicode, EntryPoint="RasGetCredentialsW")]
  static extern uint GetCredentials(string phoneBook, string entry, ref Credentials credentials);

  [DllImport("rasapi32.dll", CharSet=CharSet.Unicode, EntryPoint="RasDialW")]
  static extern uint Dial(IntPtr extensions, string phoneBook, ref DialParams parameters, uint notifierType, IntPtr notifier, out IntPtr connection);

  public static uint Connect(string entry) {
    Credentials c = new Credentials { Size=Marshal.SizeOf(typeof(Credentials)), Mask=UserName|Password|Domain, User="", Pass="", DomainName="" };
    uint result = GetCredentials(null, entry, ref c);
    if (result != 0) return result;
    if ((c.Mask & Password) == 0) return 691;
    DialParams p = new DialParams {
      Size=Marshal.SizeOf(typeof(DialParams)), Entry=entry, Phone="", Callback="",
      User=c.User, Pass=c.Pass, DomainName=c.DomainName, SubEntry=0,
      CallbackId=UIntPtr.Zero, InterfaceIndex=0
    };
    result = Dial(IntPtr.Zero, null, ref p, 0, IntPtr.Zero, out _);
    c.Pass=""; p.Pass="";
    return result;
  }
}
'@
}

$result = [NativeRasConnector]::Connect($profile)
if ($result -ne 0) { throw "Windows RAS connection failed with code $result." }
