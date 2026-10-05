using System.Diagnostics;

namespace ConnectRus;

internal static class Program
{
    const string Profile = "rus";

    [STAThread]
    static async Task Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        bool disconnect = args.Any(x => x.Equals("--disconnect", StringComparison.OrdinalIgnoreCase));
        string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "rasdial.exe");
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(Profile);
        if (disconnect) start.ArgumentList.Add("/disconnect");
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("rasdial اجرا نشد.");
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            string message = ((await stdout) + "\n" + (await stderr)).Trim();
            if (process.ExitCode != 0)
                MessageBox.Show(message.Length == 0 ? $"خطای اتصال (کد {process.ExitCode})" : message,
                    "Connect rus", MessageBoxButtons.OK, MessageBoxIcon.Error,
                    MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Connect rus", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
