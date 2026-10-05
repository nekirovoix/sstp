using System.Diagnostics;
using System.Text;

namespace SstpQuickConnect;

internal static class Program
{
    const string ShortcutName = "SSTP Quick Connect.lnk";

    [STAThread]
    static async Task Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        string profile = ReadProfileName();
        string mode = args.FirstOrDefault()?.ToLowerInvariant() ?? "--connect";

        try
        {
            if (mode == "--remove-hotkey")
            {
                RemoveShortcut();
                ShowNotice("میانبر Ctrl+Alt+V حذف شد.", false);
                return;
            }

            EnsureShortcut();
            if (mode == "--install-hotkey")
            {
                ShowNotice($"میانبر Ctrl+Alt+V برای «{profile}» فعال شد.", false);
                return;
            }

            if (mode == "--disconnect")
            {
                var stopped = await RunRasDial(profile, "/disconnect");
                ShowNotice(stopped.Code == 0 ? $"VPN «{profile}» قطع شد." : CleanError(stopped), stopped.Code != 0);
                return;
            }

            var result = await RunRasDial(profile);
            if (result.Code != 0)
            {
                ShowNotice(CleanError(result), true);
                return;
            }

            ShowNotice($"VPN «{profile}» متصل است.", false);
        }
        catch (Exception ex)
        {
            ShowNotice("خطا: " + ex.Message, true);
        }
    }

    static string ReadProfileName()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "profile.txt");
        if (!File.Exists(path)) return "rus";
        string value = File.ReadAllText(path, Encoding.UTF8).Trim();
        return string.IsNullOrWhiteSpace(value) ? "rus" : value;
    }

    static async Task<(int Code, string Output, string Error)> RunRasDial(string profile, string? command = null)
    {
        string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "rasdial.exe");
        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.ArgumentList.Add(profile);
        if (command is not null) info.ArgumentList.Add(command);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("rasdial اجرا نشد.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await output, await error);
    }

    static string CleanError((int Code, string Output, string Error) result)
    {
        string text = (result.Output + "\n" + result.Error).Trim();
        if (text.Length > 600) text = text[^600..];
        return string.IsNullOrWhiteSpace(text) ? $"اتصال VPN ناموفق بود (کد {result.Code})." : text;
    }

    static string ShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutName);

    static void EnsureShortcut()
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("مسیر برنامه پیدا نشد.");
        Type type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows Script Host در دسترس نیست.");
        dynamic shell = Activator.CreateInstance(type)!;
        dynamic shortcut = shell.CreateShortcut(ShortcutPath);
        shortcut.TargetPath = executable;
        shortcut.WorkingDirectory = AppContext.BaseDirectory;
        shortcut.Description = "Connect saved SSTP VPN profile";
        shortcut.Hotkey = "CTRL+ALT+V";
        shortcut.Save();
    }

    static void RemoveShortcut()
    {
        if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
    }

    static void ShowNotice(string message, bool error)
    {
        using var form = new Form
        {
            Width = 460,
            Height = 126,
            FormBorderStyle = FormBorderStyle.FixedToolWindow,
            StartPosition = FormStartPosition.Manual,
            ShowInTaskbar = false,
            TopMost = true,
            RightToLeft = RightToLeft.Yes,
            RightToLeftLayout = true,
            Text = "SSTP Quick Connect",
            BackColor = Color.White
        };
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        form.Location = new Point(area.Right - form.Width - 18, area.Bottom - form.Height - 18);
        form.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = message,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = error ? Color.FromArgb(190, 45, 45) : Color.FromArgb(35, 125, 75),
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Padding = new Padding(16)
        });
        var timer = new System.Windows.Forms.Timer { Interval = error ? 6500 : 2200 };
        timer.Tick += (_, _) => { timer.Stop(); form.Close(); };
        form.Shown += (_, _) => timer.Start();
        Application.Run(form);
    }
}
