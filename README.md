# SurfaceVnc — ریموت دسکتاپ لمسی برای سرفیس RT

**سرفیس RT را با لمس به یک ترمینال ریموت دسکتاپ تبدیل می‌کند.**
یک اپلیکیشن Windows Store (ویندوز RT 8.1، پردازنده ARM) که با پروتکل VNC/RFB به یک کامپیوتر
داخل شبکه وصل می‌شود و صفحه‌اش را لمسی کنترل می‌کند.

> Surface RT (Tegra 3 / ARM 32-bit / Windows RT 8.1) فقط می‌تواند اپلیکیشن‌های Windows Store را
> اجرا کند (برنامه‌های دسکتاپ معمولی روی RT اجرا نمی‌شوند). به همین دلیل این پروژه یک اپ
> «Windows Store» است و نه یک برنامه Win32 — دقیقاً همان چیزی که «بی‌دغدغه روی دستگاه اجرا
> می‌شود» را ممکن می‌کند.

---

## چرا VNC و نه RDP؟

| | |
|---|---|
| **RDP** | پیاده‌سازی پروتکل RDP مایکروسافت بسیار بزرگ است و ActiveX کلاینت آن برای اپ‌های Store در دسترس نیست؛ روی ویندوز RT هم `mstsc.exe` وجود ندارد. |
| **VNC (RFB)** | پروتکل RFB ساده، باز و مستند (RFC 6143) است، پیاده‌سازی‌اش روی ARM و داخل یک اپ Store کاملاً شدنی است، و سرورهای رایگان و پرسرعت برای ویندوز وجود دارد (TightVNC، UltraVNC، TigerVNC، RealVNC). |

به همین دلیل این پروژه یک کلاینت VNC است. سمت کامپیوتر فقط کافی است یک سرور VNC نصب شود.

---

## چه کارهایی انجام می‌دهد

* **لمس = ماوس:** یک انگشت روی دسکتاپ ریموت = کلیک چپ، کشیدن با یک انگشت = درگ (مثل ماوس).
* **نگه‌داشتن انگشت (۶۰۰ میلی‌ثانیه) = کلیک راست.**
* **دو انگشت:** در حالت «Fit» اسکرول می‌کند، و در حالت «1:1» که دسکتاپ بزرگ‌تر از صفحه است،
  تصویر را جابه‌جا (pan) می‌کند.
* **صفحه‌کلید لمسی ویندوز:** روی کادر «tap here to type on the PC» بزنید؛ هرچه تایپ کنید
  (از جمله متن **فارسی**، با تبدیل به keysym یونیکد) روی کامپیوتر تایپ می‌شود.
* **کلیدهای میان‌بر:** Esc، Tab، Enter، Backspace، Delete، جهت‌ها، Home/End، PgUp/PgDn،
  Ctrl/Alt/Shift (چسبان)، Win، F5، F11 و **Ctrl+Alt+Del**.
* **کلیپ‌بورد دوطرفه:** متن کپی‌شده روی کامپیوتر به کلیپ‌بورد تبلت می‌آید و با دکمه «Paste»
  کلیپ‌بورد تبلت به کامپیوتر می‌رود.
* **دو حالت نمایش:** `Fit` (کل دسکتاپ در صفحه جا می‌شود) و `1:1` (پیکسل به پیکسل + pan).
* **تمام‌صفحه** (دکمه «Full screen») و **روشن نگه‌داشتن صفحه** تا وقتی اتصال برقرار است.
* حالت **View only** (فقط تماشا) و **قطع/وصل مجدد** بدون بستن برنامه.

---

## ساختار پروژه

```
src/SurfaceVnc.Core/     هسته کلاینت VNC (مستقل از پلتفرم، قابل تست روی هر سیستمی)
  RfbProtocol / RfbClient / Decoders / Des / VncAuthentication /
  Framebuffer / PixelConverter / ViewportMapper / RemoteKeys / ...
src/SurfaceVnc.Store/    اپلیکیشن Windows Store برای ویندوز RT 8.1 (ARM)
  MainPage.xaml(.cs)     رابط لمسی
  Rfb/StreamSocketTransport.cs   سوکت WinRT
  Rfb/FramebufferRenderer.cs     رسم فریم‌بافر در WriteableBitmap
  Rfb/RemoteKeyboardBridge.cs    صفحه‌کلید لمسی/فیزیکی → کیبورد ریموت
  Package.appxmanifest   قابلیت دسترسی به شبکه محلی
tests/SurfaceVnc.Tests/  تست‌های خودکار (۴۸ تست، با یک سرور VNC واقعی روی 127.0.0.1)
tools/                   اسکریپت‌های ساخت/تست/دپلوی
docs/                    راهنمای نصب (DEPLOY.md) و مستندات پروتکل (PROTOCOL.md)
```

نکته مهم: سورس‌های `SurfaceVnc.Core` داخل پروژه Store به‌صورت **linked** استفاده می‌شوند
(یعنی همان فایل‌ها، بدون کپی). پس منطق پروتکل فقط یک‌جا نوشته شده و هم روی دستگاه و هم در
تست‌ها اجرا می‌شود.

---

## ساخت و نصب روی سرفیس RT

خلاصه: با **Visual Studio 2013 + Windows 8.1 SDK** پروژه `SurfaceVnc.sln` را باز کنید،
پیکربندی را روی **ARM** بگذارید و روی تبلت Deploy کنید.

راهنمای گام‌به‌گام (به فارسی، همراه با روش نصب بدون Visual Studio):
**[docs/DEPLOY.md](docs/DEPLOY.md)**

---

## تأیید درستی (تست‌ها)

```bash
tools/install-dotnet.sh      # فقط بار اول: نصب SDK به‌صورت محلی
tools/verify-all.sh          # اجرای همه بررسی‌ها (تست‌ها + بررسی ساختاری + کامپایل اپ)
tools/verify.sh              # فقط ساخت هسته + اجرای ۴۸ تست
tools/verify.sh Hextile      # اجرای تست‌های یک بخش خاص
tools/check-store-app.py     # بررسی ساختاری سورس‌های اپ Store (XAML، هندلرها، پروژه، manifest)
tools/check-store-compiles.sh# کامپایل سورس‌های اپ با C# 5 در برابر stub‌های WinRT
python3 tools/make-assets.py # ساخت تصاویر کاشی/اسپلش اپ
```

**۱) تست‌های هسته:** یک **سرور VNC واقعی** روی `127.0.0.1` بالا می‌آید و کلاینت واقعی از طریق
TCP به آن وصل می‌شود: handshake نسخه ۳.۳ و ۳.۸، احراز هویت VNC (با بردار مرجع مستقل از
OpenSSL)، همه انکودینگ‌های پشتیبانی‌شده (Raw، CopyRect، Hextile، RRE، CoRRE)، فرمت‌های پیکسل
۸/۱۶/۲۴/۳۲ بیت، رویدادهای ماوس/کیبورد/کلیپ‌بورد و نگاشت لمس به مختصات ریموت.

**۲) بررسی ساختاری اپ:** درستی XML فایل‌های XAML/manifest/csproj، وجود همه handlerهایی که در
XAML ارجاع شده‌اند با امضای درست، اینکه پروژه Store همه فایل‌های سورس را شامل می‌شود،
قابلیت‌های شبکه در manifest، و اینکه سورس‌های هسته چیز غیرمجاز برای WinRT استفاده نکنند.

**۳) کامپایل اپ:** اپ واقعی فقط روی ویندوز با Windows 8.1 SDK کامپایل می‌شود، ولی سورس‌های آن
روی هر سیستمی با **C# 5** و در برابر `tests/winrt-stubs` (مدل کامپایلی APIهای WinRT که اپ
استفاده می‌کند) کامپایل و type-check می‌شوند. فیلدهای تولیدشده از XAML هم با اسکریپت
`tools/generate-xaml-fields.py` ساخته می‌شوند، پس اگر نام یا نوع کنترلی با XAML نخواند،
همان‌جا خطا می‌گیرید.

---

## محدودیت‌ها (صادقانه)

* فقط **VNC/RFB**؛ پروتکل RDP پشتیبانی نمی‌شود.
* انکودینگ‌های `Tight`، `ZRLE` و `TRLE` پیاده نشده‌اند؛ سرور به‌طور خودکار روی Hextile/Raw
  کار می‌کند (برای شبکه محلی کاملاً کافی است).
* احراز هویت: `None` و `VNC password` (همان DES کلاسیک). روی شبکه محلی مطمئن استفاده کنید؛
  برای اینترنت حتماً از VPN/SSH tunnel استفاده کنید.
* فقط شبکه محلی/اینترانت (قابلیت `privateNetworkClientServer`).
* پروژه Store با Visual Studio **2022** ساخته نمی‌شود (مایکروسافت پشتیبانی Store 8.1 را
  حذف کرده است)؛ VS 2013/2015/2017 لازم است. جزئیات در docs/DEPLOY.md.

---

# English summary

**SurfaceVnc** is a touch-friendly **VNC (RFB) client** packaged as a **Windows Store app for
Windows RT 8.1 / ARM**, so it runs on a Surface RT (NVIDIA Tegra 3) without jailbreaking. It
connects to any VNC server (TightVNC, UltraVNC, TigerVNC, RealVNC) on the local network and turns
the tablet into a remote desktop terminal: one finger is the left mouse button, long press is a
right click, two fingers scroll or pan, the Windows on-screen keyboard types into the remote
machine (including Persian text through Unicode keysyms), and the clipboard works in both
directions.

* Platform independent client core: `src/SurfaceVnc.Core` (netstandard2.0, unit tested).
* Windows RT app: `src/SurfaceVnc.Store` (C#/XAML, links the core sources, ARM configurations).
* Build: open `SurfaceVnc.sln` in Visual Studio 2013 with the Windows 8.1 SDK, select **ARM** and
  deploy. See [docs/DEPLOY.md](docs/DEPLOY.md).
* Verify: `tools/verify-all.sh` runs the 48 automated tests (the real client driven against a real
  RFB server over TCP), structural checks of the app sources, and a C# 5 compile check of the app
  layer against `tests/winrt-stubs` (a compile time model of the WinRT APIs it uses).
* Protocol notes and the list of implemented RFB features: [docs/PROTOCOL.md](docs/PROTOCOL.md).
