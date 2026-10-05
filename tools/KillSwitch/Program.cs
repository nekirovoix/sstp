using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SstpKillSwitch;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new KillSwitchForm());
    }
}

internal sealed class KillSwitchForm : Form
{
    const string Domain = "t.navar-abyari.ir";
    readonly Label status = new() { Dock = DockStyle.Top, Height = 72, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
    readonly Button enable = new() { Text = "فعال‌کردن Kill Switch", Width = 190, Height = 44 };
    readonly Button disable = new() { Text = "بازگردانی اینترنت و فایروال", Width = 210, Height = 44 };

    public KillSwitchForm()
    {
        Text = "SSTP Kill Switch";
        ClientSize = new Size(460, 190);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        Controls.Add(status);
        var panel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 92, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(14), WrapContents = false };
        panel.Controls.Add(enable); panel.Controls.Add(disable); Controls.Add(panel);
        enable.Click += async (_, _) => await EnableAsync();
        disable.Click += async (_, _) => await DisableAsync();
        status.Text = File.Exists(StatePath) ? "Kill Switch قبلاً فعال شده است." : "Kill Switch غیرفعال است.";
    }

    static string StatePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SstpKillSwitch", "firewall-state.json");

    async Task EnableAsync()
    {
        SetBusy(true, "در حال تنظیم Windows Firewall...");
        try
        {
            string ip = (await Dns.GetHostAddressesAsync(Domain)).First(x => x.AddressFamily == AddressFamily.InterNetwork).ToString();
            string script = $$"""
$ErrorActionPreference='Stop'
$group='SSTP Kill Switch'
$state='{{Ps(StatePath)}}'
$dir=Split-Path $state
New-Item -ItemType Directory -Path $dir -Force | Out-Null
if(-not (Test-Path $state)){
  $saved=@{}
  Get-NetFirewallProfile | ForEach-Object { $saved[$_.Name]=$_.DefaultOutboundAction.ToString() }
  $saved | ConvertTo-Json | Set-Content -Encoding UTF8 $state
}
Get-NetFirewallRule -Group $group -ErrorAction SilentlyContinue | Remove-NetFirewallRule
New-NetFirewallRule -DisplayName 'SSTP Allow VPN tunnel traffic' -Group $group -Direction Outbound -Action Allow -InterfaceType RemoteAccess -Profile Any | Out-Null
New-NetFirewallRule -DisplayName 'SSTP Allow server TCP 443' -Group $group -Direction Outbound -Action Allow -Protocol TCP -RemotePort 443 -RemoteAddress '{{ip}}' -Profile Any | Out-Null
$dns=Get-DnsClientServerAddress | ForEach-Object { $_.ServerAddresses } | Where-Object { $_ -and $_ -notmatch '^127\.' -and $_ -ne '::1' } | Sort-Object -Unique
if($dns){
  New-NetFirewallRule -DisplayName 'SSTP Allow DNS UDP' -Group $group -Direction Outbound -Action Allow -Protocol UDP -RemotePort 53 -RemoteAddress $dns -Profile Any | Out-Null
  New-NetFirewallRule -DisplayName 'SSTP Allow DNS TCP' -Group $group -Direction Outbound -Action Allow -Protocol TCP -RemotePort 53 -RemoteAddress $dns -Profile Any | Out-Null
}
New-NetFirewallRule -DisplayName 'SSTP Allow DHCPv4' -Group $group -Direction Outbound -Action Allow -Protocol UDP -LocalPort 68 -RemotePort 67 -Profile Any | Out-Null
New-NetFirewallRule -DisplayName 'SSTP Allow DHCPv6' -Group $group -Direction Outbound -Action Allow -Protocol UDP -LocalPort 546 -RemotePort 547 -Profile Any | Out-Null
Set-NetFirewallProfile -Name Domain,Private,Public -DefaultOutboundAction Block
""";
            var result = await RunPowerShell(script);
            if (result.Code != 0) throw new Exception(result.Text);
            status.Text = $"Kill Switch فعال شد؛ فقط SSTP به {ip}:443 و ترافیک VPN مجاز است.";
            status.ForeColor = Color.FromArgb(35, 125, 75);
        }
        catch (Exception ex)
        {
            status.Text = "خطا: " + ex.Message;
            status.ForeColor = Color.Firebrick;
        }
        finally { SetBusy(false, status.Text); }
    }

    async Task DisableAsync()
    {
        SetBusy(true, "در حال بازگردانی تنظیمات قبلی...");
        try
        {
            string script = $$"""
$ErrorActionPreference='Stop'
$group='SSTP Kill Switch'
$state='{{Ps(StatePath)}}'
Get-NetFirewallRule -Group $group -ErrorAction SilentlyContinue | Remove-NetFirewallRule
if(Test-Path $state){
  $saved=Get-Content $state -Raw | ConvertFrom-Json
  $saved.PSObject.Properties | ForEach-Object { Set-NetFirewallProfile -Name $_.Name -DefaultOutboundAction $_.Value }
  Remove-Item $state -Force
}else{
  Set-NetFirewallProfile -Name Domain,Private,Public -DefaultOutboundAction Allow
}
""";
            var result = await RunPowerShell(script);
            if (result.Code != 0) throw new Exception(result.Text);
            status.Text = "قوانین Kill Switch حذف و دسترسی عادی اینترنت بازگردانی شد.";
            status.ForeColor = Color.FromArgb(35, 125, 75);
        }
        catch (Exception ex)
        {
            status.Text = "خطا: " + ex.Message;
            status.ForeColor = Color.Firebrick;
        }
        finally { SetBusy(false, status.Text); }
    }

    void SetBusy(bool busy, string text)
    {
        enable.Enabled = disable.Enabled = !busy;
        status.Text = text;
        UseWaitCursor = busy;
    }

    static string Ps(string value) => value.Replace("'", "''");

    static async Task<(int Code, string Text)> RunPowerShell(string script)
    {
        string wrapped = "$ProgressPreference='SilentlyContinue'\ntry {\n" + script + "\n} catch { Write-Output ('ERROR: '+$_.Exception.Message); exit 1 }";
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(wrapped));
        string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-OutputFormat", "Text", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("PowerShell اجرا نشد.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, ((await stdout) + "\n" + (await stderr)).Trim());
    }
}
