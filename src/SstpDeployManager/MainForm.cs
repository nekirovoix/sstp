using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace SstpDeployManager;

public sealed class MainForm : Form
{
    readonly TextBox sshHost = Box("203.0.113.10"), sshPort = Box("22"), sshUser = Box("root"), sshPass = SecretBox();
    readonly TextBox domain = Box("vpn.example.com"), vpnUser = Box("vpnuser"), vpnPass = SecretBox(), profile = Box("SSTP VPN");
    readonly Button deploy = PrimaryButton("۱  اتصال SSH و دپلوی SSTP"), create = PrimaryButton("۲  ساخت پروفایل و اتصال ویندوز");
    readonly Button connect = SecondaryButton("اتصال"), disconnect = SecondaryButton("قطع اتصال"), clearLog = SecondaryButton("پاک‌کردن گزارش");
    readonly RichTextBox log = new() { ReadOnly = true, BackColor = Color.FromArgb(249,248,247), BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 9), DetectUrls = false };
    readonly Label status = new() { Text = "آماده", AutoSize = false, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.FromArgb(70,161,113), Font = new Font("Segoe UI", 9, FontStyle.Bold) };
    readonly CheckBox showPasswords = new() { Text = "نمایش رمزها", AutoSize = true };
    readonly CheckBox autoConnect = new() { Text = "پس از ساخت، متصل شود", Checked = true, AutoSize = true };

    public MainForm()
    {
        Text = "SSTP Deploy Manager";
        ClientSize = new Size(940, 760);
        MinimumSize = new Size(900, 700);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White;
        Font = new Font("Segoe UI", 9.5f);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        AutoScaleMode = AutoScaleMode.Dpi;

        var title = new Label { Text = "مدیریت نصب و اتصال SSTP", Font = new Font("Segoe UI", 20, FontStyle.Bold), ForeColor = Color.FromArgb(44,44,43), AutoSize = true, Location = new Point(638, 22) };
        var sub = new Label { Text = "دپلوی امن روی Ubuntu و ساخت خودکار پروفایل VPN در ویندوز", ForeColor = Color.FromArgb(125,122,117), AutoSize = true, Location = new Point(541, 62) };
        Controls.Add(title); Controls.Add(sub);

        var serverCard = Card("سرور Ubuntu", new Point(28, 96), new Size(430, 304));
        AddField(serverCard, "آدرس SSH", sshHost, 56); AddField(serverCard, "پورت", sshPort, 100);
        AddField(serverCard, "نام کاربری SSH", sshUser, 144); AddField(serverCard, "رمز SSH", sshPass, 188);
        deploy.SetBounds(24, 242, 382, 44); serverCard.Controls.Add(deploy); Controls.Add(serverCard);

        var vpnCard = Card("پروفایل SSTP", new Point(480, 96), new Size(432, 304));
        AddField(vpnCard, "دامنه VPN", domain, 56); AddField(vpnCard, "نام کاربری VPN", vpnUser, 100);
        AddField(vpnCard, "رمز VPN", vpnPass, 144); AddField(vpnCard, "نام پروفایل", profile, 188);
        create.SetBounds(24, 242, 384, 44); vpnCard.Controls.Add(create); Controls.Add(vpnCard);

        showPasswords.Location = new Point(745, 414); autoConnect.Location = new Point(548, 414);
        Controls.Add(showPasswords); Controls.Add(autoConnect);
        connect.SetBounds(28, 410, 112, 38); disconnect.SetBounds(150, 410, 112, 38);
        Controls.Add(connect); Controls.Add(disconnect);

        var logTitle = new Label { Text = "گزارش عملیات", Font = new Font("Segoe UI", 11, FontStyle.Bold), AutoSize = true, Location = new Point(809, 470) };
        clearLog.SetBounds(28, 462, 112, 34); status.SetBounds(150, 462, 640, 34);
        log.SetBounds(28, 506, 884, 220); log.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        Controls.Add(logTitle); Controls.Add(clearLog); Controls.Add(status); Controls.Add(log);

        showPasswords.CheckedChanged += (_,_) => { sshPass.UseSystemPasswordChar = !showPasswords.Checked; vpnPass.UseSystemPasswordChar = !showPasswords.Checked; };
        deploy.Click += async (_,_) => await DeployAsync();
        create.Click += async (_,_) => await CreateProfileAsync();
        connect.Click += async (_,_) => await ConnectVpnAsync();
        disconnect.Click += async (_,_) => await DisconnectVpnAsync();
        clearLog.Click += (_,_) => log.Clear();
        vpnPass.Text = GenerateVpnPassword();
        Append("برنامه آماده است. یک رمز VPN امن به‌صورت خودکار تولید شد؛ می‌توانید آن را تغییر دهید.");
    }

    static TextBox Box(string text="") => new() { Text = text, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10) };
    static TextBox SecretBox() => new() { UseSystemPasswordChar = true, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10) };
    static Button PrimaryButton(string text) => new() { Text = text, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(39,131,222), ForeColor = Color.White, Font = new Font("Segoe UI", 10, FontStyle.Bold), Cursor = Cursors.Hand, FlatAppearance = { BorderSize = 0 } };
    static Button SecondaryButton(string text) => new() { Text = text, FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Color.FromArgb(44,44,43), Cursor = Cursors.Hand, FlatAppearance = { BorderColor = Color.FromArgb(230,229,227) } };
    static Panel Card(string heading, Point p, Size s)
    {
        var panel = new Panel { Location=p, Size=s, BackColor=Color.FromArgb(249,248,247), BorderStyle=BorderStyle.FixedSingle };
        panel.Controls.Add(new Label { Text=heading, Font=new Font("Segoe UI",12,FontStyle.Bold), ForeColor=Color.FromArgb(44,44,43), AutoSize=true, Location=new Point(s.Width-145,16) });
        return panel;
    }
    static void AddField(Control parent, string label, TextBox box, int y)
    {
        var l = new Label { Text=label, AutoSize=false, TextAlign=ContentAlignment.MiddleRight, ForeColor=Color.FromArgb(80,78,74) };
        l.SetBounds(parent.Width-145,y,120,30); box.SetBounds(24,y,260,30); box.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;
        parent.Controls.Add(l); parent.Controls.Add(box);
    }

    void Append(string text)
    {
        if (InvokeRequired) { BeginInvoke(() => Append(text)); return; }
        log.AppendText($"[{DateTime.Now:HH:mm:ss}] {text.Trim()}\n"); log.SelectionStart=log.TextLength; log.ScrollToCaret();
    }
    void SetBusy(bool busy, string text)
    {
        if (InvokeRequired) { BeginInvoke(() => SetBusy(busy,text)); return; }
        deploy.Enabled=create.Enabled=connect.Enabled=disconnect.Enabled=!busy;
        status.Text=text; status.ForeColor=busy?Color.FromArgb(213,128,59):Color.FromArgb(70,161,113);
        UseWaitCursor=busy;
    }
    bool ValidateInputs(bool needSsh)
    {
        if (!Regex.IsMatch(domain.Text.Trim(), @"^(?=.{1,253}$)([A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)+[A-Za-z]{2,63}$")) { MessageBox.Show("دامنه معتبر نیست.","خطا",MessageBoxButtons.OK,MessageBoxIcon.Warning); return false; }
        if (!Regex.IsMatch(vpnUser.Text.Trim(), @"^[A-Za-z0-9._-]{1,32}$")) { MessageBox.Show("نام کاربری VPN معتبر نیست.","خطا",MessageBoxButtons.OK,MessageBoxIcon.Warning); return false; }
        if (string.IsNullOrEmpty(vpnPass.Text))
        {
            if (needSsh)
            {
                vpnPass.Text = GenerateVpnPassword();
                Append("رمز VPN خالی بود؛ یک رمز امن به‌صورت خودکار تولید شد.");
            }
            else
            {
                MessageBox.Show("رمز VPN را وارد کنید.","خطا",MessageBoxButtons.OK,MessageBoxIcon.Warning); return false;
            }
        }
        if (string.IsNullOrWhiteSpace(profile.Text)) { MessageBox.Show("نام پروفایل را وارد کنید.","خطا",MessageBoxButtons.OK,MessageBoxIcon.Warning); return false; }
        if (needSsh && (string.IsNullOrWhiteSpace(sshHost.Text)||string.IsNullOrWhiteSpace(sshUser.Text)||string.IsNullOrEmpty(sshPass.Text)||!int.TryParse(sshPort.Text,out var p)||p<1||p>65535)) { MessageBox.Show("اطلاعات SSH کامل یا معتبر نیست.","خطا",MessageBoxButtons.OK,MessageBoxIcon.Warning); return false; }
        return true;
    }

    async Task DeployAsync()
    {
        if(!ValidateInputs(true)) return;
        SetBusy(true,"در حال اتصال و دپلوی...");
        Append($"اتصال به {sshHost.Text.Trim()}:{sshPort.Text.Trim()} با کاربر {sshUser.Text.Trim()}...");
        try
        {
            await Task.Run(() =>
            {
                var auth = new PasswordAuthenticationMethod(sshUser.Text.Trim(), sshPass.Text);
                var info = new ConnectionInfo(sshHost.Text.Trim(), int.Parse(sshPort.Text), sshUser.Text.Trim(), auth) { Timeout=TimeSpan.FromSeconds(15) };
                using var client = new SshClient(info);
                client.HostKeyReceived += (_,e) =>
                {
                    var fp="SHA256:"+Convert.ToBase64String(SHA256.HashData(e.HostKey)).TrimEnd('=');
                    DialogResult r=DialogResult.No;
                    Invoke(() => r=MessageBox.Show($"اثر انگشت سرور:\n{fp}\n\nآیا به این سرور اعتماد دارید؟","تأیید کلید میزبان SSH",MessageBoxButtons.YesNo,MessageBoxIcon.Question));
                    e.CanTrust=r==DialogResult.Yes;
                };
                client.Connect();
                Append("اتصال SSH برقرار شد و کلید میزبان تأیید شد.");
                string id=Guid.NewGuid().ToString("N");
                string scriptPath=$"/tmp/sstp-deploy-{id}.sh", cfgPath=$"/tmp/sstp-deploy-{id}.conf";
                string b64=Convert.ToBase64String(Encoding.UTF8.GetBytes(DeployScript.Text.Replace("\r\n","\n")));
                string cfg=$"DOMAIN_B64={B64(domain.Text.Trim())}\nVPN_USER_B64={B64(vpnUser.Text.Trim())}\nVPN_PASS_B64={B64(vpnPass.Text)}\n";
                string cfg64=Convert.ToBase64String(Encoding.UTF8.GetBytes(cfg));
                Exec(client,$"umask 077; printf %s {Q(b64)} | base64 -d > {scriptPath}; printf %s {Q(cfg64)} | base64 -d > {cfgPath}; chmod 700 {scriptPath}; chmod 600 {cfgPath}",false);
                string run=sshUser.Text.Trim()=="root"?$"bash {scriptPath} {cfgPath}":$"sudo -n bash {scriptPath} {cfgPath}";
                using var cmd=client.CreateCommand(run); cmd.CommandTimeout=TimeSpan.FromMinutes(15); string output=cmd.Execute();
                foreach(var line in output.Split('\n',StringSplitOptions.RemoveEmptyEntries)) Append(line);
                if(!string.IsNullOrWhiteSpace(cmd.Error)) foreach(var line in cmd.Error.Split('\n',StringSplitOptions.RemoveEmptyEntries)) Append("! "+line);
                Exec(client,$"rm -f {scriptPath} {cfgPath}",false);
                if(cmd.ExitStatus!=0 || !output.Contains("DEPLOY_OK")) throw new Exception($"دپلوی کامل نشد (کد {cmd.ExitStatus}). گزارش را بررسی کنید.");
                client.Disconnect();
            });
            Append("دپلوی SSTP با موفقیت تمام شد."); status.Text="دپلوی موفق";
            MessageBox.Show("SSTP روی سرور آماده است. اکنون پروفایل ویندوز را بسازید.","موفق",MessageBoxButtons.OK,MessageBoxIcon.Information);
        }
        catch(Exception ex){ Append("خطا: "+ex.Message); status.Text="خطا در دپلوی"; status.ForeColor=Color.FromArgb(229,100,88); MessageBox.Show(ex.Message,"خطای دپلوی",MessageBoxButtons.OK,MessageBoxIcon.Error); }
        finally{ SetBusy(false,status.Text); }
    }


    static string GenerateVpnPassword()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        Span<byte> data = stackalloc byte[16];
        RandomNumberGenerator.Fill(data);
        var result = new char[data.Length];
        for (int i = 0; i < data.Length; i++) result[i] = chars[data[i] % chars.Length];
        return new string(result);
    }

    static string B64(string s)=>Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
    static string Q(string s)=>"'"+s.Replace("'","'\\''")+"'";
    static string Exec(SshClient c,string command,bool fail=true){ using var x=c.CreateCommand(command); x.CommandTimeout=TimeSpan.FromSeconds(60); var o=x.Execute(); if(fail&&x.ExitStatus!=0) throw new Exception(x.Error); return o; }

    async Task CreateProfileAsync()
    {
        if(!ValidateInputs(false)) return;
        SetBusy(true,"در حال ساخت پروفایل ویندوز...");
        try
        {
            string ps=$$"""
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$name=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{B64(profile.Text.Trim())}}'))
$server=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{B64(domain.Text.Trim())}}'))
$user=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{B64(vpnUser.Text.Trim()+"@SSTP")}}'))
$pass=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{B64(vpnPass.Text)}}'))
$old=Get-VpnConnection -Name $name -ErrorAction SilentlyContinue
if($old){ rasdial.exe $name /disconnect | Out-Null; Remove-VpnConnection -Name $name -Force }
Add-VpnConnection -Name $name -ServerAddress $server -TunnelType Sstp -EncryptionLevel Optional -AuthenticationMethod MSChapv2 -RememberCredential -SplitTunneling $false -Force | Out-Null
Write-Output "PROFILE_OK: $name -> $server"
if({{(autoConnect.Checked?"$true":"$false")}}){
  $r=& rasdial.exe $name $user $pass 2>&1
  $r | ForEach-Object { Write-Output $_ }
  if($LASTEXITCODE -ne 0){ exit $LASTEXITCODE }
}
""";
            var result=await RunPowerShell(ps);
            Append(result.Output); if(result.Code!=0) throw new Exception(result.Error+"\n"+result.Output);
            Append("پروفایل ویندوز با موفقیت ساخته شد."); status.Text="پروفایل آماده است";
            MessageBox.Show("پروفایل SSTP ساخته شد"+(autoConnect.Checked?" و اتصال انجام شد.":"."),"موفق",MessageBoxButtons.OK,MessageBoxIcon.Information);
        }
        catch(Exception ex){ Append("خطا: "+ex.Message); status.Text="خطا در ساخت پروفایل"; status.ForeColor=Color.FromArgb(229,100,88); MessageBox.Show(ex.Message,"خطا",MessageBoxButtons.OK,MessageBoxIcon.Error); }
        finally{ SetBusy(false,status.Text); }
    }
    async Task ConnectVpnAsync(){ if(!ValidateInputs(false))return; SetBusy(true,"در حال اتصال..."); try{var r=await RunProcess("rasdial.exe",new[]{profile.Text.Trim(),vpnUser.Text.Trim()+"@SSTP",vpnPass.Text});Append(r.Output);if(r.Code!=0)throw new Exception(r.Output+r.Error);status.Text="متصل";}catch(Exception ex){Append("خطا: "+ex.Message);status.Text="اتصال ناموفق";}finally{SetBusy(false,status.Text);} }
    async Task DisconnectVpnAsync(){ SetBusy(true,"در حال قطع اتصال...");try{var r=await RunProcess("rasdial.exe",new[]{profile.Text.Trim(),"/disconnect"});Append(r.Output);status.Text="قطع شد";}finally{SetBusy(false,status.Text);} }

    static async Task<(int Code,string Output,string Error)> RunPowerShell(string script)
    {
        string enc=Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        string exe=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32","WindowsPowerShell","v1.0","powershell.exe");
        return await RunProcess(exe,new[]{"-NoLogo","-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass","-EncodedCommand",enc});
    }
    static async Task<(int Code,string Output,string Error)> RunProcess(string exe,IEnumerable<string> args)
    {
        var p=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}; foreach(var a in args)p.ArgumentList.Add(a);
        using var proc=Process.Start(p)??throw new Exception("اجرای فرایند ممکن نشد.");
        var o=proc.StandardOutput.ReadToEndAsync();var e=proc.StandardError.ReadToEndAsync();await proc.WaitForExitAsync();return(proc.ExitCode,await o,await e);
    }
}
