# راهنمای نصب روی سرفیس RT (ویندوز RT 8.1)

این راهنما دو بخش دارد:

* **بخش ۱ — آماده کردن کامپیوتری که می‌خواهید کنترلش کنید** (سرور VNC)
* **بخش ۲ — ساخت و نصب اپ روی سرفیس RT**

> **چرا برنامه معمولی (exe) نمی‌شود؟**
> ویندوز RT فقط اپلیکیشن‌های Windows Store را اجرا می‌کند (برنامه‌های Win32 روی RT فقط اگر
> مایکروسافت امضا کرده باشد اجرا می‌شوند). پس این پروژه یک اپ Store است و روش نصبش هم
> «Deploy/Sideload» است، نه کپی کردن یک exe.

---

## بخش ۱ — روی کامپیوتر مقصد: نصب سرور VNC

۱. یکی از این سرورهای رایگان را نصب کنید:

| سرور | نکته |
|---|---|
| **TightVNC** | سبک و ساده؛ گزینه «Require password» را فعال کنید. |
| **UltraVNC** | امکانات بیشتر (مثلاً انتقال فایل). |
| **TigerVNC** | پایدار و متن‌باز. |
| **RealVNC** | نسخه رایگان هم دارد. |

۲. در تنظیمات سرور:
   * یک **رمز** بگذارید (Password). اگر رمز نگذارید، اپ با احراز هویت `None` هم وصل می‌شود
     ولی روی شبکه، بی‌رمز گذاشتن توصیه نمی‌شود.
   * گزینه «Allow loopback / Allow connections from other computers» را فعال کنید.
   * اگر می‌خواهید همان نشست فعلی ویندوز را ببینید (نه یک دسکتاپ جدا)، گزینه‌های مربوط به
     «Service mode / mirror driver» را فعال کنید.
   * پورت پیش‌فرض **5900** است (اگر نمایشگر دوم دارید: 5901 و ...).

۳. در **فایروال ویندوز**، پورت 5900 ورودی را برای شبکه محلی (Private) اجازه دهید. سریع‌ترین
   راه، اجرای این دستور در PowerShell با دسترسی Administrator روی کامپیوتر است:

```powershell
New-NetFirewallRule -DisplayName "VNC server (LAN)" -Direction Inbound -Protocol TCP -LocalPort 5900 -Action Allow -Profile Private
```

۴. **IP کامپیوتر** را یادداشت کنید (در `cmd` بزنید `ipconfig`؛ مثلاً `192.168.1.10`).

---

## بخش ۲ — ساخت و نصب اپ روی سرفیس RT

### پیش‌نیازهای کامپیوتر سازنده (کامپیوتر شما، نه سرفیس)

* ویندوز ۸.۱ / ۱۰ / ۱۱ روی x64
* **Visual Studio 2013** (توصیه‌شده) — یا ۲۰۱۵ / ۲۰۱۷ که هنوز پروژه‌های Store 8.1 را باز می‌کنند
* در هنگام نصب VS، این‌ها را انتخاب کنید:
  * `Tools for Windows 8.1 Store apps` (یا `Windows 8.1 SDK`)
  * `C# / XAML` برای Store apps
* ⚠️ **Visual Studio 2022** پروژه‌های Store 8.1 را پشتیبانی نمی‌کند (مایکروسافت حذفش کرده).
  با VS2022 خطای «unsupported project type» می‌گیرید.

### روش A — Deploy مستقیم از Visual Studio (ساده‌ترین)

۱. `SurfaceVnc.sln` را در Visual Studio باز کنید.
۲. در نوار ابزار، پیکربندی را روی **Release** و پلتفرم را روی **ARM** بگذارید
   (اگر ARM در لیست نیست: `Configuration Manager` → `Active solution platform` → `ARM`).
۳. روی سرفیس RT یک **Developer License** بگیرید:
   * در سرفیس: `PowerShell` (از منوی استارت، Windows PowerShell) را باز کنید و بزنید:
     ```powershell
     Show-WindowsDeveloperLicenseRegistration
     ```
     و با حساب مایکروسافت وارد شوید. (در ویندوز RT 8.1 معمولاً همین کافی است؛ چون نصب
     اپ‌های Trusted به‌طور پیش‌فرض مجاز است.)
   * یا در Visual Studio: `Project → Store → Acquire Developer License`.
۴. در Visual Studio: کلیک راست روی پروژه `SurfaceVnc.Store` → `Properties` → تب `Debug` →
   `Target device` = **Remote Machine** و در فیلد `Remote machine` آی‌پی سرفیس RT را وارد کنید.
   (روی سرفیس: `Settings → Change PC settings → Network` یا در PowerShell بزنید `ipconfig`.)
   اگر پرسید، با همان حساب مایکروسافت Pair کنید.
۵. `Build → Deploy SurfaceVnc.Store` را بزنید (یا F5 برای اجرای مستقیم روی تبلت).
۶. تمام! اپ `SurfaceVnc` در صفحه Start سرفیس ظاهر می‌شود.

### روش B — ساخت بسته `.appx` و نصب دستی (بدون نیاز به Remote Debugging)

۱. در Visual Studio: `Project → Store → Create App Packages...`
۲. `No` (برای آپلود در Store) → Next.
۳. فقط **ARM** را تیک بزنید، `Release` → Create.
۴. پوشه‌ای مثل `AppPackages\SurfaceVnc.Store_1.0.0.0_ARM_Test\` ساخته می‌شود که شامل:
   * `SurfaceVnc.Store_1.0.0.0_ARM.appx`
   * `SurfaceVnc.Store_1.0.0.0_ARM.cer` (گواهی امضای آزمایشی)
   * `Add-AppDevPackage.ps1`
۵. هر سه فایل را به سرفیس RT کپی کنید (USB یا شبکه).
۶. روی سرفیس، PowerShell را باز کنید و در پوشه فایل‌ها اجرا کنید:

```powershell
# گواهی امضا را در Trusted People نصب می‌کند و بعد بسته را نصب می‌کند
Set-ExecutionPolicy -Scope Process Bypass
.\Add-AppDevPackage.ps1
```

اگر خواستید دستی انجام دهید:

```powershell
Import-Certificate -FilePath .\SurfaceVnc.Store_1.0.0.0_ARM.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
Add-AppxPackage -Path .\SurfaceVnc.Store_1.0.0.0_ARM.appx
```

اسکریپت آماده در مخزن: [`deploy/Deploy-ToSurfaceRT.ps1`](../deploy/Deploy-ToSurfaceRT.ps1)
(این اسکریپت را روی خود سرفیس اجرا کنید؛ گواهی را نصب و بسته را رجیستر می‌کند).

### روش C — اگر Visual Studio 2013 ندارید

یک پروژه خالی بسازید و فایل‌های ما را به آن اضافه کنید:

۱. در VS (نسخه‌ای که Store 8.1 را پشتیبانی می‌کند): `New Project → Templates → Visual C# →
   Windows Store → Blank App (XAML)` با نام `SurfaceVnc.Store`.
۲. فایل‌های زیر را از مخزن به پروژه کپی/اضافه کنید (Add → Existing Item):
   * `App.xaml`, `App.xaml.cs`, `MainPage.xaml`, `MainPage.xaml.cs`
   * `Rfb\StreamSocketTransport.cs`, `Rfb\FramebufferRenderer.cs`, `Rfb\RemoteKeyboardBridge.cs`
   * `Settings\ConnectionSettingsStore.cs`
   * تمام فایل‌های `src\SurfaceVnc.Core\**\*.cs` (به‌صورت Link، با همان ساختار پوشه)
   * `Assets\*.png`
۳. در `Package.appxmanifest` (تب Capabilities) تیک **Private Networks (Client & Server)** و
   **Internet (Client)** را بزنید. (روی ARM هم deploy کنید.)

---

## بخش ۳ — استفاده

۱. روی سرفیس RT اپ **SurfaceVnc** را باز کنید.
۲. آدرس (IP) و پورت کامپیوتر را وارد کنید (پیش‌فرض 5900) و رمز VNC را بزنید.
۳. `Connect` را بزنید. چند لحظه بعد دسکتاپ کامپیوتر روی تبلت ظاهر می‌شود.

### حرکات لمسی

| حرکت | نتیجه |
|---|---|
| یک انگشت، ضربه | کلیک چپ |
| یک انگشت، کشیدن | درگ (کلیک چپ نگه‌داشته) |
| نگه‌داشتن انگشت (~۰.۶ ثانیه) | کلیک راست |
| دو انگشت، بالا/پایین (حالت Fit) | اسکرول (چرخ ماوس) |
| دو انگشت، کشیدن (حالت 1:1) | جابه‌جا کردن تصویر (pan) |
| دکمه `Right` | کلیک راست در آخرین نقطه لمس‌شده |
| دکمه `Scroll ▲/▼` | اسکرول دقیق (یک پله) |
| کادر «tap here to type on the PC» | باز شدن صفحه‌کلید لمسی ویندوز → تایپ روی کامپیوتر |
| دکمه `Keys` | نوار کلیدهای ویژه (Esc، Ctrl، Alt، Ctrl+Alt+Del، F5، ...) |
| دکمه `Fit` / `1:1` | تغییر حالت نمایش |
| دکمه `Type text` | یک پنجره برای تایپ/پیست متن طولانی (مناسب متن فارسی) |
| دکمه `Paste` | فرستادن کلیپ‌بورد تبلت به کامپیوتر |
| دکمه `Full screen` | تمام‌صفحه شدن اپ |

### اگر وصل نشد

| مشکل | راه‌حل |
|---|---|
| «Could not connect ...» | IP را چک کنید؛ سرور VNC روی کامپیوتر اجرا و پورت در فایروال باز باشد؛ هر دو دستگاه روی یک شبکه باشند (مثلاً سرفیس روی Wi-Fi و کامپیوتر روی همان مودم). |
| «Wrong VNC password» | رمز همان رمزی است که در تنظیمات سرور VNC گذاشته‌اید (رمز ویندوز نیست). |
| تصویر نمی‌آید ولی اتصال برقرار است | بعضی سرورها «Loopback» را در حالت Service فعال نمی‌کنند؛ گزینه mirror driver/service mode را در سرور فعال کنید. |
| صفحه کند است | حالت نمایش را روی `Fit` بگذارید، در سرور VNC کیفیت/انکودینگ را «Hextile» یا «Raw» بگذارید، و اگر Wi-Fi ضعیف است نزدیک مودم شوید. |
| اپ نصب نمی‌شود | Developer License بگیرید یا گواهی امضا را در `Trusted People` نصب کنید (روش B). |

---

## نکته امنیتی

احراز هویت VNC (همان DES کلاسیک) از نظر رمزنگاری ضعیف است و ترافیک VNC رمزنگاری نمی‌شود.
پس:

* فقط در شبکه محلی/اینترانت استفاده کنید،
* اگر می‌خواهید از اینترنت وصل شوید، از **VPN** یا **SSH tunnel** استفاده کنید:

```bash
# روی تبلت امکان تونل زدن ساده نیست، ولی روی کامپیوتر/روتر می‌توانید پورت را تونل کنید
ssh -L 5900:192.168.1.10:5900 user@gateway
```

* در سرور VNC فقط به IPهای شبکه محلی اجازه اتصال بدهید.

---

## ساخت اپ روی یک کامپیوتر دیگر (فقط هسته، بدون Store app)

اگر فقط می‌خواهید منطق پروتکل را تست کنید (بدون ساخت اپ سرفیس):

```bash
tools/install-dotnet.sh     # نصب .NET SDK به‌صورت محلی
tools/verify.sh             # اجرای تست‌ها
```

روی ویندوز هم همین دو اسکریپت با Git Bash / WSL کار می‌کنند (یا دستی: `dotnet test`-مانند
`dotnet run --project tests/SurfaceVnc.Tests`).
