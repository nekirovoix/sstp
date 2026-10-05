using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace ConnectRus;

internal static class Program
{
    const string Profile = "rus";
    const int RasCmUserName = 0x1, RasCmPassword = 0x2, RasCmDomain = 0x4;
    const uint ErrorBufferTooSmall = 603;

    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        try
        {
            if (IsConnected(Profile)) return;
            uint result = ConnectWithSavedCredentials(Profile);
            if (result != 0) throw new Exception(GetRasError(result));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Connect rus", MessageBoxButtons.OK, MessageBoxIcon.Error,
                MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
        }
    }

    static uint ConnectWithSavedCredentials(string entry)
    {
        var credentials = new RasCredentials
        {
            Size = Marshal.SizeOf<RasCredentials>(),
            Mask = RasCmUserName | RasCmPassword | RasCmDomain,
            UserName = string.Empty,
            Password = string.Empty,
            Domain = string.Empty
        };
        uint result = RasGetCredentials(null, entry, ref credentials);
        if (result != 0) return result;
        if ((credentials.Mask & RasCmPassword) == 0)
            throw new Exception("برای این پروفایل رمز ذخیره‌شده‌ای در Windows RAS وجود ندارد. ابتدا یک‌بار از Settings متصل شوید و Remember my sign-in info را فعال کنید.");

        var parameters = new RasDialParams
        {
            Size = Marshal.SizeOf<RasDialParams>(),
            EntryName = entry,
            PhoneNumber = string.Empty,
            CallbackNumber = string.Empty,
            UserName = credentials.UserName,
            Password = credentials.Password,
            Domain = credentials.Domain,
            SubEntry = 0,
            CallbackId = UIntPtr.Zero,
            InterfaceIndex = 0
        };
        result = RasDial(IntPtr.Zero, null, ref parameters, 0, IntPtr.Zero, out _);
        credentials.Password = string.Empty;
        parameters.Password = string.Empty;
        return result;
    }

    static bool IsConnected(string entry)
    {
        int structSize = Marshal.SizeOf<RasConn>();
        int bufferSize = structSize;
        int count;
        var connections = NewConnections(1, structSize);
        uint result = RasEnumConnections(connections, ref bufferSize, out count);
        if (result == ErrorBufferTooSmall)
        {
            int capacity = Math.Max(count, (bufferSize + structSize - 1) / structSize);
            connections = NewConnections(capacity, structSize);
            bufferSize = capacity * structSize;
            result = RasEnumConnections(connections, ref bufferSize, out count);
        }
        if (result != 0) return false;
        return connections.Take(count).Any(x => string.Equals(x.EntryName, entry, StringComparison.OrdinalIgnoreCase));
    }

    static RasConn[] NewConnections(int count, int size)
    {
        var items = new RasConn[count];
        for (int i = 0; i < items.Length; i++) items[i].Size = size;
        return items;
    }

    static string GetRasError(uint code)
    {
        var text = new StringBuilder(512);
        return RasGetErrorString(code, text, text.Capacity) == 0
            ? $"RAS error {code}: {text}"
            : new Win32Exception((int)code).Message;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct RasCredentials
    {
        public int Size, Mask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string UserName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string Password;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)] public string Domain;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct RasDialParams
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string EntryName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 129)] public string PhoneNumber;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 129)] public string CallbackNumber;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string UserName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string Password;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)] public string Domain;
        public int SubEntry;
        public UIntPtr CallbackId;
        public int InterfaceIndex;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct RasConn
    {
        public int Size;
        public IntPtr Handle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string EntryName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 17)] public string DeviceType;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 129)] public string DeviceName;
    }

    [DllImport("rasapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RasGetCredentialsW")]
    static extern uint RasGetCredentials(string? phoneBook, string entry, ref RasCredentials credentials);

    [DllImport("rasapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RasDialW")]
    static extern uint RasDial(IntPtr extensions, string? phoneBook, ref RasDialParams parameters, uint notifierType, IntPtr notifier, out IntPtr connection);

    [DllImport("rasapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RasEnumConnectionsW")]
    static extern uint RasEnumConnections([In, Out] RasConn[] connections, ref int bufferSize, out int connectionCount);

    [DllImport("rasapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RasGetErrorStringW")]
    static extern uint RasGetErrorString(uint error, StringBuilder buffer, int bufferSize);
}
