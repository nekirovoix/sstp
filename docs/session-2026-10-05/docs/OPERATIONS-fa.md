# نصب، استفاده و تست

## پیش‌نیاز

Windows با Windows PowerShell و NetSecurity/VpnClient، پروفایل SSTP موجود و اعتبارنامه ذخیره‌شده در ویندوز. پروفایل باید با اجرای دستی `rasphone.exe -d "rus"` و زدن Connect وصل شود. دسترسی Administrator برای تغییر فایروال لازم است. در اتصال RDP/AnyDesk یا روی سیستم سازمانی بدون هماهنگی تست نکنید.

## Launcher

فایل `scripts/Connect-rus-v1.3.ps1` را به `C:\Connect-rus-v1.3.ps1` کپی کنید؛ ممکن است کپی به ریشه C تأیید Administrator بخواهد. نیازی به تغییر ACL کل درایو نیست. مسیر دیگری مانند پوشه کاربر نیز قابل استفاده است، با اصلاح Target.

آزمایش با پنجره قابل مشاهده:

```powershell
powershell.exe -NoLogo -NoProfile -NoExit -ExecutionPolicy Bypass -File "C:\Connect-rus-v1.3.ps1"
```

Target شورتکات:

```text
C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "C:\Connect-rus-v1.3.ps1"
```

Start in: `C:\`. لاگ: `%LOCALAPPDATA%\Connect-rus.log`. برای پروفایل دیگر مقدار `$vpnName = 'rus'` در نسخه کپی‌شده را تغییر دهید؛ این نسخه پارامتر Profile ندارد. هنگام اجرای Launcher با ماوس/صفحه‌کلید کار نکنید؛ اتوماسیون به فوکوس پنجره و دکمه پیش‌فرض متکی است. پروفایل‌های AllUser در این Launcher صریحاً بررسی نمی‌شوند. اسکریپت ثبت موفقیت یا خطا می‌کند؛ بستن تمام دیالوگ‌ها تضمین نشده است.

## Kill Switch

فایل را در `C:\SstpKillSwitch-v1.2.ps1` قرار دهید. دستورات را در PowerShell ادمین اجرا کنید. اگر از حالت غیرادمین اجرا شود، عملیات غیر Status درخواست UAC می‌کند؛ بسته‌شدن فرآیند والد، پایان موفق عملیات ادمین را ثابت نمی‌کند.

فرمان بازیابی عادی را قبل از شروع به‌صورت آفلاین نگه دارید:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\SstpKillSwitch-v1.2.ps1" -Action Disable
```

ابتدا VPN موردنظر را متصل کنید و Status بگیرید، سپس Enable:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\SstpKillSwitch-v1.2.ps1" -Action Status
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\SstpKillSwitch-v1.2.ps1" -Action Enable
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\SstpKillSwitch-v1.2.ps1" -Action Status
```

Enable اولین SSTP متصل را انتخاب می‌کند؛ برای جلوگیری از ابهام فقط یک پروفایل SSTP متصل باشد. پروفایل‌ها باید فایروال فعال داشته باشند؛ اسکریپت Enabled را True نمی‌کند. وضعیت مورد انتظار: Block در سه پروفایل، وجود قوانین و وضعیت ذخیره‌شده. صرفاً وجود قوانین اثبات کارکرد نیست.

### تست

1. هنگام اتصال، فرمان زیر باید True بدهد؛ InterfaceAlias و SourceAddress را نیز بررسی کنید.
2. VPN را قطع کنید، اما اینترنت پایه و Kill Switch را روشن نگه دارید؛ فرمان باید تکمیل و False شود. در دوره قطع VPN این گفتگو ممکن است در دسترس نباشد.
3. VPN همان سرور را با `rasphone.exe -d "نام-پروفایل"` دوباره وصل کنید؛ انتظار True و رابط VPN است. اگر سرور متفاوت است مجوز موجود الزاماً آن را پوشش نمی‌دهد.
4. Disable را اجرا، Status را بررسی و سپس VPN را قطع کنید؛ اینترنت عادی باید برگردد.

```powershell
Test-NetConnection 1.1.1.1 -Port 443
```

False فقط مسدودشدن آن مقصد/پورت را نشان می‌دهد. برای بررسی واقعی، چند مقصد، IPv6، DNS و برنامه‌های دارای قوانین Allow موجود باید جداگانه آزموده شوند.

### بازیابی

Disable قوانین گروه `SSTP Kill Switch` را حذف و DefaultOutboundAction ذخیره‌شده را بازیابی می‌کند. فایل وضعیت در `%ProgramData%\SstpKillSwitch\firewall-state.json` است. بدون وضعیت، Disable پیش‌فرض‌ها را حدس نمی‌زند.

اگر بازیابی عادی شکست خورد و بازکردن خروجی ضروری بود:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\SstpKillSwitch-v1.2.ps1" -Action EmergencyReset
```

EmergencyReset هر سه پیش‌فرض خروجی را Allow کرده و وضعیت ذخیره‌شده را حذف می‌کند؛ بازگردانی دقیق نیست و روی سیستم سازمانی/با سیاست قبلی Block خطر دارد. Restore defaults ویندوز نیز بازیابی اختصاصی نیست: قوانین سفارشی دیگر را ریست می‌کند و فایل وضعیت اسکریپت را هماهنگ نمی‌کند.

شورتکات Disable: همان مسیر powershell.exe بالا، با `-NoProfile -ExecutionPolicy Bypass -File "C:\SstpKillSwitch-v1.2.ps1" -Action Disable`. اسکریپت برای عملیات تغییر فایروال UAC درخواست می‌کند.
