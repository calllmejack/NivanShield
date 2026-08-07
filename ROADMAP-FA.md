# نقشه توسعه Nivan Shield

## تکمیل‌شده تا نسخه 5.0

- هسته مشترک SSH و sing-box با پروفایل‌های چندگانه
- Neko core قابل‌حمل و Headless داخل بسته
- System Proxy و TUN برای SSH و V2Ray
- Connection Health Center، Jitter، Failure Rate و Speed Test
- Smart Connect و Failover خودکار
- Split Tunneling برای Proxy و TUN
- بازیابی LAN Proxy پس از Crash و Network Lock محدود به Proxy-aware apps
- Subscriptionهای مدیریت‌شده با URL رمزنگاری‌شده و Refresh خودکار
- بررسی و دانلود آپدیت HTTPS همراه با SHA-256
- رابط ساده با سه مسیر اصلی SSH، Config و Subscription
- فعال‌شدن خودکار اولین کانفیگ واردشده
- تأیید اینترنت واقعی V2Ray پیش از نمایش Connected
- Bootstrap مستقیم دامنه سرور برای جلوگیری از حلقه DNS
- ساختار اولیه مناسب GitHub و Build خودکار ویندوز
- چهار حالت ساده‌ی Browser، Selected Apps، Whole Windows و System Proxy
- عیب‌یابی مرحله‌ای Provider تا اینترنت داخل تونل
- DNS Center با تست، snapshot و Restore خودکار
- Providerهای مستقل SSH، sing-box، پروکسی خارجی و Psiphon
- Psiphon اختیاری با بررسی Publisher و pin شدن فایل‌های رسمی
- سیاست Secure Core و حذف اجرای هسته‌ی سفارشی
- ناوبری چهارقسمتی Home، Connections، DNS و Settings با انتخاب پروفایل روی Home
- بارگذاری رابط XAML از داخل EXE به‌جای فایل قابل‌تغییر هنگام اجرا
- رابط دو‌زبانه فارسی/English با تغییر فوری زبان از بالای برنامه
- Home ساده با دکمه‌های مستقیم SSH، V2Ray، DNS و Psiphon
- حالت سازگار با NekoRay برای روشن‌بودن هم‌زمان TUN و System Proxy

## گام‌های بعدی پیشنهادی

1. **Installer و امضای دیجیتال:** ساخت MSIX/Setup، گواهی Code Signing و Manifest آپدیت امضاشده.
2. **تفکیک دسترسی Administrator:** اجرای UI با دسترسی عادی و انتقال TUN، DNS و Recovery به یک Windows Service امضاشده و محدود.
3. **SSH fingerprint pinning:** جایگزین امن اختیاری برای حذف Host Key و `accept-new` در استفاده عمومی.
4. **Kill Switch سراسری:** Ruleهای Windows Filtering Platform در همان سرویس محدود؛ کاملاً جدا از Network Lock فعلی و با Restore تضمین‌شده.
5. **Backup رمزنگاری‌شده:** Export/Import پروفایل‌ها بدون افشای Secret و با رمز اصلی کاربر.
6. **Plugin SDK عمومی:** قرارداد امضاشده برای Providerهای آینده بدون اجازه‌ی اجرای DLL/EXE ناشناس.

جست‌وجوی Telegram فعلاً طبق تصمیم محصول از نقشه نسخه ۶ حذف شده است.

## مدل درآمد سالم

- **Free:** اتصال SSH/V2Ray، Import و مدیریت پایه
- **Pro:** Smart Connect، Failover، Health History، Split Tunneling و Backup رمزنگاری‌شده
- **Team:** پروفایل‌های مدیریت‌شده، سیاست مرکزی و پشتیبانی قراردادی

درآمد بهتر است از مجوز نرم‌افزار و پشتیبانی باشد، نه تبلیغ داخل VPN یا فروش داده کاربر. پیش از فروش عمومی باید GPL اجزای همراه، قوانین سرویس و شرایط بازپرداخت بررسی شوند.
