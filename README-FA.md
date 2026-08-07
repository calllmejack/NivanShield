# Nivan Shield 6.0.5

Nivan Shield یک پروژه متن‌باز و ناشناس ویندوزی برای اتصال با SSH، کانفیگ‌های V2Ray، Subscription، پروکسی خارجی و Psiphon است. هیچ نام سازنده، سرور، نام کاربری، کانفیگ یا Credential شخصی در نسخه عمومی وجود ندارد.

> هدف: تسهیل دسترسی آدم‌ها به دنیای آزاد اینترنت

## قابلیت‌های نسخه ۶.۰

- تغییر فوری زبان فارسی/English از نوار بالای برنامه و ذخیره انتخاب کاربر؛
- یک دکمه Power سراسری در نوار بالا برای اتصال و قطع اتصال از تمام صفحه‌ها؛
- دکمه‌های مستقیم SSH، V2Ray، DNS و Psiphon در Home به‌جای لیست پروفایل فعال؛
- حالت پیشنهادی Whole Windows با روشن‌شدن هم‌زمان TUN و System Proxy، مشابه روند قبلی NekoRay؛
- انتخاب واقعی سرویس با دکمه‌های Home و نمایش جداگانه پروفایل‌های V2Ray؛
- دکمه بازگردانی اینترنت عادی برای قطع Nivan، پاک‌کردن LAN Proxy متعلق به برنامه و Restore امن DNS، بدون حذف پروفایل‌ها؛
- ترمیم خودکار Credential گم‌شده‌ی کانفیگ V2Ray هنگام Import مجدد یا Refresh اشتراک؛
- ورود QR از فایل PNG/JPG به‌صورت کاملاً آفلاین و تشخیص خودکار کانفیگ V2Ray یا لینک Subscription؛
- انتخاب چند پروفایل با Ctrl/Shift، دکمه Select all و حذف گروهی همراه Credentialهای همان پروفایل‌ها؛
- میانبرهای پیش‌فرض Ctrl+A، Delete، Ctrl+D، Ctrl+N، Ctrl+I و Ctrl+Shift+I که همگی از Settings قابل تغییرند؛
- نمایش Subscriptionها در بالای صفحه V2Ray و Import فوری کانفیگ‌ها بدون مرحله جداگانه Refresh یا Save Profile؛
- اتصال با دابل‌کلیک روی کانفیگ، قطع امن Provider قبلی بدون خروج از صفحه Connections و مرتب‌سازی بر اساس پیشنهاد، پینگ، نام، دسته یا پروتکل؛
- پشتیبانی از XHTTP با Xray-core رسمی و hash-pinned در کنار هسته Neko برای سایر کانفیگ‌ها؛
- شروع نسخه عمومی با فرم SSH خالی و بدون Host، Username، کانفیگ V2Ray یا Subscription آماده؛

- چهار حالت روشن برای عبور ترافیک: فقط مرورگر امن، برنامه‌های انتخابی، کل دستگاه با TUN و Windows System Proxy؛
- اتصال SSH با همان روند قبلی، رمز ذخیره‌شده با Windows DPAPI و اتصال مجدد خودکار؛
- واردکردن `vmess://`، `vless://`، `trojan://` و `ss://` یا لینک Subscription؛
- افزودن SOCKS5، HTTP یا HTTPS Proxy خارجی؛
- Provider مستقل Psiphon با انتخاب دستی هسته و فایل تنظیمات رسمی؛
- DNS Center برای تست، اعمال و بازگردانی DNS؛
- عیب‌یابی مرحله‌ای Provider، سرور، پروکسی محلی، اینترنت داخل تونل و Routing؛
- تست Ping، Jitter، Packet failure و Speed Test؛
- بررسی SHA-256 هسته‌ی همراه با fingerprint کامپایل‌شده پیش از اجرا.
- قراردادن رابط XAML داخل خود EXE تا فایل رابط قابل‌تغییر کنار برنامه با دسترسی Administrator بارگذاری نشود.

بخش جست‌وجوی Telegram عمداً در نسخه ۶ وجود ندارد.

## اجرای سریع

برای ساخت روی Windows 10/11 فایل زیر را اجرا کنید:

```bat
Build.cmd
```

Build علاوه بر کامپایل، SHA-256 فایل‌های runtime همراه را بررسی می‌کند. سپس `NivanShield.exe` را اجرا کنید. برای TUN یا تغییر DNS، برنامه طبق Manifest از Windows دسترسی Administrator می‌گیرد.

## سه مسیر اصلی اتصال

### SSH

Host، Port، Username و Password یا Private Key را در صفحه SSH وارد کنید. برنامه SOCKS5 را روی پورت محلی می‌سازد و در صورت نیاز routing داخلی را برای TUN یا System Proxy اجرا می‌کند. رمز داخل تنظیمات به‌شکل متن ساده ذخیره نمی‌شود و با DPAPI همان حساب Windows محافظت می‌شود.

حذف Host Key قدیمی پیش از اتصال، برای سازگاری با روند قدیمی برنامه حفظ شده است. این گزینه در برابر جایگزینی سرور یا حمله‌ی واسط ضعیف‌تر از pin کردن fingerprint است؛ برای استفاده عمومی، مرحله بعدی امنیتی پروژه باید افزودن fingerprint pinning اختیاری باشد.

### V2Ray و Subscription

در صفحه V2Ray می‌توانید یک یا چند لینک کانفیگ را Paste کنید و **Import & use** را بزنید. برای Subscription لینک را در کارت بالای همان صفحه وارد و **Add & import now** را بزنید. برنامه همان لحظه اشتراک را دانلود و کانفیگ‌ها را آماده می‌کند؛ Refresh و Save Profile جداگانه لازم نیست. سپس کانفیگ را انتخاب و Connect کنید، یا در Connections روی آن دابل‌کلیک کنید. اگر Credential یک پروفایل در دسترس نباشد، Import دوباره لینک یا Refresh اشتراک همان پروفایل را بدون Duplicate ترمیم می‌کند.

برای QR روی **Import QR image** بزنید یا `Ctrl+Shift+I` را فشار دهید. تصویر فقط داخل خود برنامه و با Decoder همراه خوانده می‌شود. اگر QR شامل `vmess://`، `vless://`، `trojan://` یا `ss://` باشد به‌عنوان کانفیگ وارد می‌شود؛ لینک HTTP/HTTPS به‌عنوان Subscription تشخیص داده می‌شود.

در صفحه Connections با Ctrl یا Shift چند پروفایل را انتخاب کنید. `Ctrl+A` همه را انتخاب و کلید `Delete` آن‌ها را گروهی حذف می‌کند. اگر همه پروفایل‌ها حذف شوند، برنامه فقط یک فرم SSH کاملاً خالی برای ساخت اتصال بعدی ایجاد می‌کند.

### Psiphon

به‌دلیل امنیت و مجوز توزیع، Nivan هیچ فایل Psiphon را خودکار دانلود نمی‌کند. در صفحه Psiphon باید `ConsoleClient.exe` و `client.config` رسمی را انتخاب کنید. برنامه امضای Publisher را بررسی و سپس SHA-256 همان فایل را pin می‌کند. فایل تغییرکرده یا جایگزین‌شده اجرا نمی‌شود.

## حالت‌های عبور ترافیک

| حالت | کاربرد | اثر روی Windows |
|---|---|---|
| فقط مرورگر امن | فقط Edge یا Chrome ایزوله | بدون تغییر System Proxy و بدون TUN |
| برنامه‌های انتخابی | چند EXE مشخص از داخل TUN | TUN فقط برای Processهای انتخابی |
| کل دستگاه | بازی، ابزار توسعه و همه برنامه‌ها | TUN و System Proxy هم‌زمان فعال؛ نیازمند Administrator |
| System Proxy | مرورگرها و برنامه‌های سازگار | Windows LAN Proxy فعال |

در حالت «فقط مرورگر امن»، دکمه‌ی Open protected browser فقط Edge یا Chrome نصب‌شده با Publisher معتبر را باز می‌کند و مسیر دلخواه کاربر را اجرا نمی‌کند.

## DNS Center

نسخه ۶ این گزینه‌ها را دارد:

- Shecan: `178.22.122.100` و `185.51.200.2`
- 403.online: `10.202.10.202` و `10.202.10.102`
- Radar Game: `10.202.10.10` و `10.202.10.11`
- Electro: `78.157.42.100` و `78.157.42.101`
- Begzar: `185.55.226.26` و `185.55.225.25`
- Quad9 و Cloudflare
- DNS سفارشی IPv4

قبل از Apply می‌توانید DNS انتخابی یا همه گزینه‌ها را تست کنید. Nivan وضعیت قبلی کارت‌های شبکه را پیش از اولین تغییر snapshot می‌گیرد؛ بنابراین **Restore previous DNS** تنظیم DHCP یا DNS استاتیک قبلی را بازمی‌گرداند. بازگردانی هنگام Disconnect یا بعد از Crash نیز قابل انتخاب است.

DNS یک VPN نیست، IP شما را مخفی نمی‌کند و ممکن است بعضی سرویس‌ها فقط برای رفع محدودیت دامنه مناسب باشند.

## عیب‌یابی و تست سرعت

دکمه Diagnose این مراحل را جداگانه گزارش می‌کند:

1. وضعیت Provider؛
2. دسترسی به Endpoint؛
3. بازبودن پروکسی محلی؛
4. دسترسی واقعی اینترنت از داخل SOCKS؛
5. وضعیت حالت Routing و TUN.

Health Center نیز Latency، Jitter، نرخ شکست و تست Quick/Full سرعت را نگه می‌دارد. Speed Test فقط با اقدام کاربر اجرا می‌شود.

## مدل امنیتی

- هیچ Telemetry، تبلیغ، استخراج کانفیگ یا جست‌وجوی مخفی شبکه وجود ندارد.
- Password، UUID، کلیدها و URLهای Subscription در لاگ عادی نوشته نمی‌شوند.
- Secretها با DPAPI و مخصوص همان حساب Windows ذخیره می‌شوند.
- کانفیگ‌های runtime با ACL محدود ساخته و پس از Disconnect حذف می‌شوند.
- پاسخ‌گوی SSH AskPass داخل همان `NivanShield.exe` است و helper اجرایی جداگانه‌ای برای جایگزین‌شدن وجود ندارد.
- دانلود Subscription و Update دارای محدودیت حجم، بدون Redirect و با ممنوعیت مقصد خصوصی/محلی است.
- هسته همراه با fingerprint کامپایل‌شده و `tools/integrity.sha256` کنترل می‌شود؛ اجرای هسته سفارشی در Secure Mode غیرفعال است.
- Psiphon فقط پس از بررسی Publisher و pin شدن hash اجرا می‌شود.
- رابط اجرایی از resource داخلی EXE بارگذاری می‌شود و تغییر فایل XAML سورس پس از Build روی برنامه‌ی ساخته‌شده اثر ندارد.
- هیچ فرمان یا EXE دلخواه از داده‌ی Subscription اجرا نمی‌شود.

جزئیات و محدودیت‌ها در `SECURITY.md` نوشته شده‌اند.

## ساخت و انتشار متن‌باز

Workflow موجود در `.github/workflows/windows-build.yml` روی Windows برنامه را Build می‌کند و دو فایل `NivanShield-6.0.5-windows-x64.zip` و `NivanShield-6.0.5-source.zip` می‌سازد. قبل از Push عمومی، هرگز فایل‌های `%LOCALAPPDATA%\NivanShield`، log، credential، private key یا لینک زنده Subscription را داخل Repository قرار ندهید.

نیازمندی‌ها:

- Windows 10 یا 11؛
- .NET Framework 4.7.2 یا جدیدتر؛
- Windows OpenSSH Client برای SSH؛
- Administrator برای TUN و تغییر DNS.

## مجوز

کد Nivan Shield تحت GNU GPL v3 منتشر می‌شود. مجوزها و Noticeهای هسته همراه باید هنگام بازنشر حفظ شوند.
