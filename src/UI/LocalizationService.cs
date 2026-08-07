using System;
using System.Collections;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Nivan.Shield.UI
{
    /// <summary>
    /// Lightweight runtime localization for the portable XAML application.
    /// English remains the stable source language; Persian translations are
    /// applied without reloading the window, so live connection state is kept.
    /// </summary>
    public sealed class LocalizationService
    {
        private sealed class ElementState
        {
            public string TextSource;
            public string TextApplied;
            public string ContentSource;
            public string ContentApplied;
            public string HeaderSource;
            public string HeaderApplied;
            public string ToolTipSource;
            public string ToolTipApplied;
            public FlowDirection OriginalFlowDirection;
            public TextAlignment OriginalTextAlignment;
            public bool LayoutCaptured;
        }

        private readonly Dictionary<DependencyObject, ElementState> _states =
            new Dictionary<DependencyObject, ElementState>();
        private readonly Dictionary<string, string> _persian =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _english =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly KeyValuePair<string, string>[] _prefixes;

        public LocalizationService()
        {
            string[] lines = PersianCatalog.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                int separator = line.IndexOf('\t');
                if (separator <= 0 || separator >= line.Length - 1) continue;
                string english = line.Substring(0, separator);
                string persian = line.Substring(separator + 1);
                _persian[english] = persian;
                if (!_english.ContainsKey(persian)) _english[persian] = english;
            }
            _prefixes = new KeyValuePair<string, string>[]
            {
                Pair("Advanced settings for ", "تنظیمات پیشرفته برای "),
                Pair("Active ", "فعال: "),
                Pair("Connect ", "اتصال به "),
                Pair("Testing ", "در حال آزمایش "),
                Pair("Last checked ", "آخرین بررسی "),
                Pair("DNS: ", "DNS: "),
                Pair("Engine: ", "هسته: "),
                Pair("Version ", "نسخه "),
                Pair("Imported ", "وارد شد: "),
                Pair("Updated ", "به‌روزرسانی شد: "),
                Pair("Connection failed: ", "اتصال ناموفق بود: "),
                Pair("Subscription failed: ", "خطای اشتراک: "),
                Pair("DNS change failed: ", "تغییر DNS ناموفق بود: ")
                ,Pair("Completed: ", "انجام شد: ")
                ,Pair("Needs repair: ", "نیازمند ترمیم: ")
                ,Pair("Repair required: ", "ترمیم لازم است: ")
                ,Pair("Selected profiles: ", "پروفایل‌های انتخاب‌شده: ")
                ,Pair("QR recognized: ", "QR شناسایی شد: ")
                ,Pair("An encrypted password is saved for profile '", "رمز رمزنگاری‌شده برای این پروفایل ذخیره شده: '")
                ,Pair("Connection settings saved for ", "تنظیمات اتصال ذخیره شد: ")
                ,Pair("Core check failed: ", "بررسی هسته ناموفق بود: ")
                ,Pair("Could not reach the ", "دسترسی برقرار نشد: ")
                ,Pair("Delete profile '", "حذف پروفایل '")
                ,Pair("Downloading and verifying version ", "در حال دانلود و بررسی نسخه ")
                ,Pair("Forget the saved SSH host key for ", "حذف Host Key ذخیره‌شده SSH برای ")
                ,Pair("No password is saved for profile '", "برای این پروفایل رمزی ذخیره نشده: '")
                ,Pair("Refreshing ", "در حال به‌روزرسانی ")
                ,Pair("Remove the encrypted saved SSH password for '", "حذف رمز ذخیره‌شده و رمزنگاری‌شده SSH برای '")
                ,Pair("Remove the managed subscription '", "حذف اشتراک مدیریت‌شده '")
                ,Pair("SSH credentials are managed per SSH profile. The active profile uses ", "اطلاعات SSH جداگانه برای هر پروفایل نگهداری می‌شود. پروفایل فعال: ")
                ,Pair("Subscription saved securely. ", "اشتراک با امنیت ذخیره شد. ")
                ,Pair("The active profile uses ", "پروفایل فعال استفاده می‌کند از: ")
                ,Pair("Traffic is being forced through 127.0.0.1:", "ترافیک از این مسیر اجباری عبور می‌کند: 127.0.0.1:")
                ,Pair("Update check failed: ", "بررسی به‌روزرسانی ناموفق بود: ")
                ,Pair("Update download failed: ", "دانلود به‌روزرسانی ناموفق بود: ")
                ,Pair("Verified package ready: ", "بسته تأییدشده آماده است: ")
                ,Pair("VPN ", "VPN ")
            };
        }

        public bool IsPersian(string language)
        {
            return String.Equals(language, "fa", StringComparison.OrdinalIgnoreCase);
        }

        public string Translate(string source, string language)
        {
            if (!IsPersian(language) || String.IsNullOrEmpty(source)) return source ?? String.Empty;
            string translated;
            if (_persian.TryGetValue(source, out translated)) return translated;

            if (source.IndexOf("  •  ", StringComparison.Ordinal) >= 0)
            {
                string[] parts = source.Split(new string[] { "  •  " }, StringSplitOptions.None);
                bool changed = false;
                for (int index = 0; index < parts.Length; index++)
                {
                    string part = Translate(parts[index], language);
                    changed = changed || !String.Equals(part, parts[index], StringComparison.Ordinal);
                    parts[index] = part;
                }
                if (changed) return String.Join("  •  ", parts);
            }

            foreach (KeyValuePair<string, string> pair in _prefixes)
            {
                if (source.StartsWith(pair.Key, StringComparison.Ordinal))
                    return pair.Value + source.Substring(pair.Key.Length);
            }
            return source;
        }

        public void Apply(Window window, string language)
        {
            if (window == null) return;
            bool persian = IsPersian(language);
            window.Title = persian ? "نیوان شیلد" : "Nivan Shield";
            HashSet<DependencyObject> visited = new HashSet<DependencyObject>();
            ApplyElement(window, language, persian, visited);
        }

        private void ApplyElement(
            DependencyObject element,
            string language,
            bool persian,
            HashSet<DependencyObject> visited)
        {
            if (element == null || visited.Contains(element)) return;
            visited.Add(element);
            ElementState state = StateFor(element);

            TextBlock textBlock = element as TextBlock;
            if (textBlock != null && !BindingOperations.IsDataBound(textBlock, TextBlock.TextProperty))
            {
                string source = CaptureSource(textBlock.Text, ref state.TextSource, ref state.TextApplied);
                string desired = Translate(source, language);
                if (!String.Equals(textBlock.Text, desired, StringComparison.Ordinal)) textBlock.Text = desired;
                state.TextApplied = desired;
                ApplyTextDirection(textBlock, state, persian && ContainsPersian(desired));
            }

            ContentControl contentControl = element as ContentControl;
            if (contentControl != null
                && contentControl.Content is string
                && !BindingOperations.IsDataBound(contentControl, ContentControl.ContentProperty))
            {
                string current = (string)contentControl.Content;
                string source = CaptureSource(current, ref state.ContentSource, ref state.ContentApplied);
                string desired = Translate(source, language);
                if (!String.Equals(current, desired, StringComparison.Ordinal)) contentControl.Content = desired;
                state.ContentApplied = desired;
                ApplyFlowDirection(contentControl, state, persian && ContainsPersian(desired));
            }

            HeaderedContentControl headered = element as HeaderedContentControl;
            if (headered != null && headered.Header is string)
            {
                string current = (string)headered.Header;
                string source = CaptureSource(current, ref state.HeaderSource, ref state.HeaderApplied);
                string desired = Translate(source, language);
                if (!String.Equals(current, desired, StringComparison.Ordinal)) headered.Header = desired;
                state.HeaderApplied = desired;
            }

            FrameworkElement frameworkElement = element as FrameworkElement;
            if (frameworkElement != null && frameworkElement.ToolTip is string)
            {
                string current = (string)frameworkElement.ToolTip;
                string source = CaptureSource(current, ref state.ToolTipSource, ref state.ToolTipApplied);
                string desired = Translate(source, language);
                if (!String.Equals(current, desired, StringComparison.Ordinal)) frameworkElement.ToolTip = desired;
                state.ToolTipApplied = desired;
            }

            IEnumerable children = LogicalTreeHelper.GetChildren(element);
            foreach (object child in children)
            {
                DependencyObject dependencyChild = child as DependencyObject;
                if (dependencyChild != null) ApplyElement(dependencyChild, language, persian, visited);
            }
        }

        private ElementState StateFor(DependencyObject element)
        {
            ElementState state;
            if (_states.TryGetValue(element, out state)) return state;
            state = new ElementState();
            FrameworkElement frameworkElement = element as FrameworkElement;
            if (frameworkElement != null)
            {
                state.OriginalFlowDirection = frameworkElement.FlowDirection;
                TextBlock text = frameworkElement as TextBlock;
                if (text != null) state.OriginalTextAlignment = text.TextAlignment;
                state.LayoutCaptured = true;
            }
            _states[element] = state;
            return state;
        }

        private string CaptureSource(string current, ref string source, ref string applied)
        {
            current = current ?? String.Empty;
            if (source == null || !String.Equals(current, applied, StringComparison.Ordinal))
                source = NormalizeEnglishSource(current);
            return source ?? String.Empty;
        }

        private string NormalizeEnglishSource(string current)
        {
            string english;
            if (_english.TryGetValue(current, out english)) return english;
            foreach (KeyValuePair<string, string> pair in _prefixes)
            {
                if (current.StartsWith(pair.Value, StringComparison.Ordinal))
                    return pair.Key + current.Substring(pair.Value.Length);
            }
            return current;
        }

        private static void ApplyTextDirection(TextBlock text, ElementState state, bool persian)
        {
            if (!state.LayoutCaptured) return;
            text.FlowDirection = persian ? FlowDirection.RightToLeft : state.OriginalFlowDirection;
            if (state.OriginalTextAlignment != TextAlignment.Center)
                text.TextAlignment = persian ? TextAlignment.Right : state.OriginalTextAlignment;
        }

        private static void ApplyFlowDirection(FrameworkElement element, ElementState state, bool persian)
        {
            if (!state.LayoutCaptured) return;
            element.FlowDirection = persian ? FlowDirection.RightToLeft : state.OriginalFlowDirection;
        }

        private static bool ContainsPersian(string value)
        {
            if (String.IsNullOrEmpty(value)) return false;
            foreach (char character in value)
            {
                if ((character >= '\u0600' && character <= '\u06FF')
                    || (character >= '\u0750' && character <= '\u077F')) return true;
            }
            return false;
        }

        private static KeyValuePair<string, string> Pair(string key, string value)
        {
            return new KeyValuePair<string, string>(key, value);
        }

        private const string PersianCatalog = @"
Home	خانه
Connections	اتصال‌ها
Settings	تنظیمات
Connect	اتصال
Disconnect	قطع اتصال
Disconnected	قطع است
Offline	آفلاین
Connected	متصل
Starting	در حال اتصال
Reconnecting	در حال اتصال مجدد
Stopping	در حال قطع اتصال
Connection error	خطای اتصال
Ready to connect	آماده اتصال
Connection tools	ابزارهای اتصال
Open the section you need. Account and server details can be edited at any time.	بخش موردنیاز را باز کنید. اطلاعات اکانت و سرور هر زمان قابل ویرایش است.
Account & tunnel	اکانت و تونل
Config & subscription	کانفیگ و اشتراک
Automatic	خودکار
Free fallback	اتصال رایگان جایگزین
Where should the connection work?	اتصال روی کدام بخش‌ها فعال باشد؟
Choose one simple mode. Nivan configures TUN and System Proxy automatically.	یک حالت ساده انتخاب کنید؛ نیوان TUN و System Proxy را خودکار تنظیم می‌کند.
Browser only	فقط مرورگر
Selected applications	برنامه‌های انتخابی
Whole Windows (TUN + System Proxy)	کل ویندوز (TUN + System Proxy)
System Proxy (compatible apps)	System Proxy (برنامه‌های سازگار)
Recommended: TUN routes all Windows traffic and System Proxy is enabled for compatible applications.	پیشنهادی: تمام ترافیک ویندوز از TUN عبور می‌کند و System Proxy نیز برای برنامه‌های سازگار روشن است.
Only the protected Edge/Chrome window uses the connection. Other applications stay direct.	فقط پنجره محافظت‌شده Edge یا Chrome از اتصال استفاده می‌کند و بقیه برنامه‌ها مستقیم هستند.
TUN is enabled only for the executable names you choose below.	TUN فقط برای برنامه‌هایی که پایین انتخاب می‌کنید فعال می‌شود.
Windows System Proxy is enabled for browsers and compatible applications; TUN stays off.	System Proxy ویندوز برای مرورگرها و برنامه‌های سازگار فعال می‌شود و TUN خاموش می‌ماند.
Open protected browser	بازکردن مرورگر محافظت‌شده
Add app...	افزودن برنامه...
Clear	پاک‌کردن
Diagnose	عیب‌یابی
Run diagnosis	اجرای عیب‌یابی
Connection diagnostics ready	عیب‌یابی اتصال آماده است
Checks the provider, server, local proxy, internet path, and routing mode.	سرویس، سرور، پروکسی محلی، مسیر اینترنت و حالت Routing بررسی می‌شوند.
Connection health	سلامت اتصال
Connection health not tested	سلامت اتصال هنوز آزمایش نشده
Measure real VPN latency, jitter, download, and upload.	پینگ واقعی VPN، نوسان، دانلود و آپلود را اندازه‌گیری کنید.
Open health center	بازکردن تست اتصال
Quick test	تست سریع
Full speed test	تست کامل سرعت
Cancel	لغو
Ready to test	آماده آزمایش
Connect first, then run a quick or full test.	ابتدا متصل شوید، سپس تست سریع یا کامل را اجرا کنید.
Actual VPN throughput	سرعت واقعی VPN
SERVER LATENCY	پینگ سرور
VPN LATENCY	پینگ VPN
JITTER	نوسان پینگ
DOWNLOAD	دانلود
UPLOAD	آپلود
REQUEST FAILURES	خطاهای درخواست
Recent tests	آزمایش‌های اخیر
No test is running	آزمایشی در حال اجرا نیست
Connections	اتصال‌ها
Connection profiles	پروفایل‌های اتصال
Organize SSH and V2Ray servers, choose the active route, and compare availability in parallel.	سرورهای SSH و V2Ray را مدیریت، اتصال فعال را انتخاب و وضعیت آن‌ها را مقایسه کنید.
New profile	پروفایل جدید
Test all profiles	آزمایش همه پروفایل‌ها
Search by name, category, host, or username	جست‌وجو با نام، دسته، هاست یا نام کاربری
Filter category	فیلتر دسته
Sort profiles	مرتب‌سازی پروفایل‌ها
All categories	همه دسته‌ها
Recommended	پیشنهادی
Latency	پینگ
Name	نام
Category	دسته
Server	سرور
Availability	وضعیت دسترسی
Select a profile to edit	یک پروفایل را برای ویرایش انتخاب کنید
Profile identity	مشخصات پروفایل
Profile name	نام پروفایل
Pin as a favorite profile	افزودن به علاقه‌مندی‌ها
Engine: SSH	هسته: SSH
Host	هاست
Port	پورت
Username	نام کاربری
Password	رمز عبور
Private key	کلید خصوصی
SSH port	پورت SSH
Local SOCKS5 port	پورت محلی SOCKS5
Save profile	ذخیره پروفایل
Use & connect	انتخاب و اتصال
Use & connect selects this profile and starts the connection in one step.	این پروفایل را انتخاب می‌کند و بلافاصله متصل می‌شود.
Test profile	آزمایش پروفایل
Duplicate	کپی پروفایل
Delete	حذف
Advanced SSH settings	تنظیمات پیشرفته SSH
SSH endpoint	آدرس SSH
Enter the server once, save the password, and connect.	اطلاعات سرور را وارد کنید، رمز را ذخیره و متصل شوید.
Authentication	احراز هویت
Private key path (optional)	مسیر کلید خصوصی (اختیاری)
Browse...	انتخاب...
SSH password	رمز SSH
Use the encrypted saved password automatically for connection and reconnect	استفاده خودکار از رمز ذخیره‌شده و رمزنگاری‌شده برای اتصال مجدد
Credentials are encrypted with Windows DPAPI.	اطلاعات ورود با Windows DPAPI رمزنگاری می‌شوند.
Clear saved	پاک‌کردن رمز ذخیره‌شده
Save SSH settings	ذخیره تنظیمات SSH
Save & connect	ذخیره و اتصال
Reconnect automatically after a dropped connection	اتصال مجدد خودکار پس از قطع‌شدن
Keep-alive interval (seconds)	فاصله Keep-alive (ثانیه)
Missed keep-alive limit	حد مجاز Keep-alive ناموفق
Reconnect delay (seconds)	تأخیر اتصال مجدد (ثانیه)
Host key handling	مدیریت Host Key
Always remove the old SSH host key before Connect (matches the original script)	پیش از اتصال همیشه Host Key قبلی حذف شود (مانند اسکریپت اصلی)
Auto-accept after clearing the old key	پذیرش خودکار پس از حذف کلید قبلی
Forget saved host key	حذف Host Key ذخیره‌شده
V2Ray	V2Ray
Paste a config or add a subscription. Nivan handles the core automatically.	کانفیگ یا لینک اشتراک را وارد کنید؛ نیوان هسته را خودکار مدیریت می‌کند.
Import & use	واردکردن و استفاده
Paste clipboard	جای‌گذاری از کلیپ‌بورد
Import file...	واردکردن فایل...
Paste share links here	لینک‌های اشتراک را اینجا وارد کنید
Import category	دسته کانفیگ‌ها
1. Paste a V2Ray config	۱. واردکردن کانفیگ V2Ray
2. Add a subscription link	۲. افزودن لینک اشتراک
3. External SOCKS / HTTP proxy	۳. پروکسی خارجی SOCKS / HTTP
Subscription name	نام اشتراک
Subscription URL	لینک اشتراک
Add subscription	افزودن اشتراک
Refresh automatically every 24 hours	به‌روزرسانی خودکار هر ۲۴ ساعت
Refresh selected	به‌روزرسانی انتخاب‌شده
Refresh all	به‌روزرسانی همه
Remove	حذف
External proxy	پروکسی خارجی
Protocol	پروتکل
Host or IP	هاست یا IP
Username (optional)	نام کاربری (اختیاری)
Password (optional)	رمز عبور (اختیاری)
Add external proxy	افزودن پروکسی خارجی
Portable proxy core	هسته قابل‌حمل پروکسی
Executable path	مسیر فایل اجرایی
Check core	بررسی هسته
Save core settings	ذخیره تنظیمات هسته
Restart sing-box automatically if it exits	راه‌اندازی مجدد خودکار sing-box در صورت بسته‌شدن
Use the portable core included with Nivan Shield (recommended)	استفاده از هسته همراه نیوان شیلد (پیشنهادی)
DNS Center	مرکز DNS
Test and apply popular Iranian DNS services, or restore the previous Windows setting. DNS does not hide your IP and is not a VPN.	DNSهای محبوب ایران را آزمایش و اعمال کنید یا تنظیم قبلی ویندوز را برگردانید. DNS آی‌پی را مخفی نمی‌کند و VPN نیست.
Choose a DNS provider	انتخاب سرویس DNS
Apply DNS	اعمال DNS
Test selected	آزمایش انتخاب‌شده
Test all	آزمایش همه
Restore previous DNS	بازگردانی DNS قبلی
Safe restore	بازگردانی امن
Custom DNS	DNS سفارشی
Primary IPv4	IPv4 اصلی
Secondary IPv4	IPv4 دوم
Save custom DNS	ذخیره DNS سفارشی
Restore the previous DNS whenever VPN disconnects	بازگردانی DNS قبلی هنگام قطع VPN
Restore DNS automatically after an unclean Nivan shutdown	بازگردانی خودکار DNS پس از بسته‌شدن غیرعادی نیوان
Windows DNS has not been changed by Nivan.	DNS ویندوز توسط نیوان تغییر نکرده است.
Psiphon	Psiphon
Psiphon rescue provider	اتصال جایگزین Psiphon
A free fallback provider using only an approved official Psiphon core.	اتصال رایگان جایگزین با استفاده از هسته رسمی و تأییدشده Psiphon.
Official core files	فایل‌های رسمی هسته
Select official...	انتخاب فایل رسمی...
Select config...	انتخاب کانفیگ...
Local HTTP port	پورت محلی HTTP
Region (optional, e.g. DE)	منطقه اختیاری (مثلاً DE)
Restart the Psiphon core if it exits	راه‌اندازی مجدد Psiphon در صورت بسته‌شدن
Verify files	تأیید فایل‌ها
Save Psiphon settings	ذخیره تنظیمات Psiphon
Create & use Psiphon profile	ساخت و استفاده از پروفایل Psiphon
Psiphon is not configured yet.	Psiphon هنوز تنظیم نشده است.
Settings	تنظیمات
Everyday controls are on Home. These options are changed less often.	کنترل‌های روزمره در Home هستند؛ این گزینه‌ها کمتر تغییر می‌کنند.
Application behavior	رفتار برنامه
Keep Nivan Shield running in the system tray	اجرای نیوان شیلد در System Tray ادامه پیدا کند
Start minimized in the system tray	اجرای برنامه به‌صورت Minimized
Ask before closing an active tunnel	پیش از بستن اتصال فعال سؤال شود
Automatically route Windows apps through the active V2Ray local proxy	هدایت خودکار برنامه‌های ویندوز از پروکسی محلی V2Ray
Disable 'Use a proxy server for your LAN' whenever the tunnel disconnects	خاموش‌کردن گزینه LAN Proxy هنگام قطع اتصال
Save preferences	ذخیره تنظیمات
Connection health tests	تنظیمات تست اتصال
Run a lightweight tunneled health check after connecting	اجرای تست سبک پس از اتصال
Latency samples	تعداد نمونه پینگ
Quick download (MB)	دانلود سریع (MB)
Full download (MB)	دانلود کامل (MB)
Full upload (MB)	آپلود کامل (MB)
Smart Connect & failover	اتصال هوشمند و جایگزینی خودکار
Automatically switch to another working profile when reconnecting takes too long	تعویض خودکار به پروفایل سالم در صورت طولانی‌شدن اتصال مجدد
Give favorite profiles a small priority in Smart Connect	اولویت جزئی پروفایل‌های محبوب در اتصال هوشمند
Failover delay (seconds)	تأخیر جایگزینی (ثانیه)
Max attempts	حداکثر تلاش
Network protection & split tunneling	محافظت شبکه و Split Tunneling
Repair Windows LAN Proxy automatically after an unclean app shutdown	ترمیم خودکار LAN Proxy پس از بسته‌شدن غیرعادی برنامه
Network Lock for proxy-aware Windows apps after an unexpected disconnect	قفل شبکه برای برنامه‌های سازگار با پروکسی پس از قطع ناگهانی
Enable Split Tunneling rules for System Proxy and TUN	فعال‌کردن قوانین Split Tunneling برای System Proxy و TUN
System Proxy bypass list (semicolon separated)	فهرست استثناهای System Proxy (با ; جدا شود)
TUN bypass process names	نام برنامه‌های مستثنا از TUN
TUN bypass domains	دامنه‌های مستثنا از TUN
TUN bypass IP ranges (CIDR)	محدوده IP مستثنا از TUN (CIDR)
Application updates	به‌روزرسانی برنامه
Check the configured update manifest when Nivan Shield starts	بررسی به‌روزرسانی هنگام اجرای نیوان شیلد
Update manifest URL (HTTPS)	لینک Manifest به‌روزرسانی (HTTPS)
Check now	بررسی اکنون
Download verified ZIP	دانلود ZIP تأییدشده
Open folder	بازکردن پوشه
No update source configured.	منبع به‌روزرسانی تنظیم نشده است.
Security	امنیت
Private by default	حریم خصوصی به‌صورت پیش‌فرض
Exit application	خروج از برنامه
Integrated SSH routing	Routing داخلی SSH
The bundled Neko core runs silently inside Nivan Shield. No separate window or profile list is required.	هسته Neko بدون پنجره جداگانه داخل نیوان شیلد اجرا می‌شود.
Routing modes	حالت‌های Routing
Enable integrated routing for SSH connections	فعال‌کردن Routing داخلی برای SSH
Start automatically after the SSH SOCKS port is ready	شروع خودکار پس از آماده‌شدن پورت SOCKS در SSH
System Proxy — managed automatically by the Home routing mode	System Proxy — مدیریت خودکار از Home
TUN mode — managed automatically by the Home routing mode	حالت TUN — مدیریت خودکار از Home
Integrated HTTP/SOCKS port	پورت داخلی HTTP/SOCKS
Always stop routing and remove LAN Proxy on Disconnect	توقف Routing و حذف LAN Proxy هنگام قطع اتصال
Start delay after SSH connects (seconds)	تأخیر شروع پس از اتصال SSH (ثانیه)
Save routing settings	ذخیره تنظیمات Routing
Start routing	شروع Routing
Stop routing	توقف Routing
Routing is idle	Routing غیرفعال است
Activity logs	گزارش فعالیت‌ها
Connection lifecycle, reconnect attempts, and app events.	چرخه اتصال، تلاش‌های مجدد و رویدادهای برنامه.
Open log folder	بازکردن پوشه گزارش‌ها
Copy	کپی
Recent activity	فعالیت‌های اخیر
View all logs	مشاهده همه گزارش‌ها
Smart connect	اتصال هوشمند
Test server	آزمایش سرور
No password is saved.	رمزی ذخیره نشده است.
Not tested	آزمایش نشده
Open Nivan Shield	بازکردن نیوان شیلد
Exit	خروج
No profile configured	هیچ پروفایلی تنظیم نشده است
New SSH connection	اتصال SSH جدید
No server selected	سروری انتخاب نشده است
Not running	در حال اجرا نیست
Managed by Nivan Shield	مدیریت‌شده توسط نیوان شیلد
Tunnel is offline	تونل آفلاین است
Ready to start a secure SOCKS5 tunnel.	آماده شروع تونل امن SOCKS5
Interface language	زبان رابط
English	English
فارسی	فارسی
N A V I G A T E	م س ی ر ه ا
●   Home	●   خانه
▦   Connections	▦   اتصال‌ها
DNS  DNS	DNS  دی‌ان‌اس
⚙   Settings	⚙   تنظیمات
↗   SSH	↗   SSH
◈   V2Ray	◈   V2Ray
◎   Psiphon	◎   Psiphon
⌁   Connection health	⌁   سلامت اتصال
◇   Integrated routing	◇   Routing داخلی
≡   Activity Logs	≡   گزارش فعالیت‌ها
Connection, traffic mode, and everyday tools	اتصال، حالت ترافیک و ابزارهای روزمره
Choose, test, add, or edit SSH and V2Ray profiles	انتخاب، آزمایش، افزودن یا ویرایش پروفایل‌های SSH و V2Ray
Iranian DNS profiles, testing, and safe restore	DNSهای ایران، آزمایش و بازگردانی امن
Application behavior, automation, and security	رفتار برنامه، خودکارسازی و امنیت
SSH and V2Ray connection control	کنترل اتصال SSH و V2Ray
Active connection	اتصال فعال
Open a section below, or connect the last active profile.	یکی از بخش‌های پایین را باز کنید یا آخرین پروفایل فعال را متصل کنید.
Dashboard	داشبورد
Manage	مدیریت
Automation	خودکارسازی
Mode	حالت
Provider settings	تنظیمات سرویس
Routing settings	تنظیمات Routing
How it works	نحوه کار
Test behavior	رفتار آزمایش
Reliability	پایداری
Latency variation	نوسان پینگ
Failed tunneled samples	نمونه‌های ناموفق داخل تونل
Direct TCP reachability	دسترسی مستقیم TCP
Measured inside the tunnel	اندازه‌گیری داخل تونل
Connect SSH to start System Proxy and TUN	برای شروع System Proxy و TUN به SSH متصل شوید
Choose server	انتخاب سرور
Restart delay (seconds)	تأخیر راه‌اندازی مجدد (ثانیه)
Transport / security	انتقال و امنیت
Import a config or subscription, then press Connect.	یک کانفیگ یا اشتراک وارد کنید، سپس Connect را بزنید.
Imported proxy endpoint	آدرس پروکسی واردشده
Encrypted credential available	اطلاعات ورود رمزنگاری‌شده موجود است
No managed subscription selected.	اشتراک مدیریت‌شده‌ای انتخاب نشده است.
No Psiphon executable has been approved.	فایل اجرایی Psiphon هنوز تأیید نشده است.
Select one of your saved SSH or V2Ray connections.	یکی از اتصال‌های ذخیره‌شده SSH یا V2Ray را انتخاب کنید.
Selected executable names separated by semicolons	نام برنامه‌های انتخابی با ; جدا شود
This profile has not been tested yet.	این پروفایل هنوز آزمایش نشده است.
Verify the real path through the active VPN, not only the server port.	مسیر واقعی اینترنت از VPN فعال را بررسی کنید، نه فقط پورت سرور.
The automatic check measures latency only and transfers a few bytes. Speed tests always require a button click.	بررسی خودکار فقط پینگ را با چند بایت داده اندازه می‌گیرد؛ تست سرعت فقط با کلیک کاربر اجرا می‌شود.
Quick test uses a small download and upload. Full test uses more data for a steadier result. Both are forced through Nivan's active local proxy.	تست سریع حجم کمی دانلود و آپلود دارد؛ تست کامل برای نتیجه پایدارتر داده بیشتری مصرف می‌کند. هر دو از پروکسی فعال نیوان عبور می‌کنند.
Speed measurements use Cloudflare's public speed-test endpoints. Do not run a full test on a limited data plan.	اندازه‌گیری سرعت از سرویس عمومی Cloudflare استفاده می‌کند. با اینترنت حجمی تست کامل اجرا نکنید.
Paste the complete subscription URL here	لینک کامل اشتراک را اینجا وارد کنید
Paste the HTTPS subscription link once. Servers are imported now and can be refreshed later.	لینک HTTPS اشتراک را یک‌بار وارد کنید؛ سرورها اکنون اضافه و بعداً قابل به‌روزرسانی هستند.
Paste one or more vmess://, vless://, trojan:// or ss:// links. The first imported server becomes ready to connect automatically.	یک یا چند لینک vmess، vless، trojan یا ss وارد کنید؛ اولین سرور خودکار آماده اتصال می‌شود.
Imported credentials will be encrypted separately for each profile.	اطلاعات ورود هر پروفایل جداگانه رمزنگاری می‌شود.
Protocol credentials are protected with Windows DPAPI. Re-import the link to change endpoint, transport, TLS, or Reality options.	اطلاعات ورود با Windows DPAPI محافظت می‌شود. برای تغییر سرور، انتقال، TLS یا Reality لینک را دوباره وارد کنید.
For a trusted local tool or proxy you control. Public free proxies can observe connection metadata and should not be treated as private.	فقط برای ابزار محلی یا پروکسی مورداعتماد استفاده کنید. پروکسی عمومی رایگان می‌تواند اطلاعات اتصال را مشاهده کند.
The password is encrypted with Windows DPAPI for this Windows account and is never stored in settings.json or process arguments.	رمز با Windows DPAPI برای همین حساب ویندوز رمزنگاری می‌شود و داخل settings.json یا آرگومان برنامه قرار نمی‌گیرد.
Automatic login uses a private SSH_ASKPASS helper. The password is never placed in command-line arguments, settings.json, or logs.	ورود خودکار از SSH_ASKPASS خصوصی استفاده می‌کند و رمز در خط فرمان، settings.json یا گزارش‌ها نوشته نمی‌شود.
Saved SSH passwords are protected per profile by Windows DPAPI for the current Windows user. To preserve the original working flow, the saved host-key entry is removed before every connection and the new key is accepted automatically.	رمز هر پروفایل SSH با Windows DPAPI محافظت می‌شود. برای حفظ روند قبلی، Host Key ذخیره‌شده پیش از اتصال حذف و کلید جدید خودکار پذیرفته می‌شود.
Imported proxy UUIDs and passwords are also DPAPI-protected per profile. sing-box receives them through a temporary runtime configuration that is removed after disconnect.	UUID و رمز پروکسی هر پروفایل نیز با DPAPI محافظت می‌شود. sing-box آن‌ها را از کانفیگ موقت دریافت می‌کند که پس از قطع اتصال حذف می‌شود.
UUIDs and passwords are encrypted with Windows DPAPI. The temporary sing-box JSON exists only during a connection and is deleted on Disconnect or next startup.	UUID و رمزها با Windows DPAPI رمزنگاری می‌شوند. فایل موقت sing-box فقط هنگام اتصال وجود دارد و پس از قطع یا اجرای بعدی حذف می‌شود.
Secure mode runs only the reviewed Neko core included with Nivan Shield. Custom executable loading is disabled.	حالت امن فقط هسته بررسی‌شده Neko همراه نیوان را اجرا می‌کند و اجرای فایل دلخواه غیرفعال است.
Security rule: Nivan never downloads Psiphon automatically. The selected EXE must have a valid Psiphon publisher signature; its SHA-256 fingerprint is then pinned before it can run.	قانون امنیتی: نیوان Psiphon را خودکار دانلود نمی‌کند. فایل انتخابی باید امضای معتبر Psiphon داشته باشد و SHA-256 آن پیش از اجرا ثبت می‌شود.
Network Lock is intentionally limited to apps that honor Windows System Proxy. Manual Disconnect still removes the LAN Proxy checkbox exactly as before.	Network Lock فقط برنامه‌های سازگار با System Proxy ویندوز را پوشش می‌دهد. Disconnect دستی همچنان LAN Proxy را خاموش می‌کند.
Nivan first checks server reachability, then chooses the lowest-latency usable profile. Passwords and proxy credentials stay protected by DPAPI.	نیوان ابتدا دسترسی سرورها را بررسی و سپس سالم‌ترین پروفایل با پینگ کمتر را انتخاب می‌کند. رمزها با DPAPI محافظت می‌شوند.
Nivan keeps the original SSH flow on 127.0.0.1:1080, then the bundled core creates an integrated mixed proxy and optional TUN adapter. DNS uses encrypted DoH through the SSH route. The core configuration is temporary and is removed after Disconnect.	نیوان روند اصلی SSH را روی 127.0.0.1:1080 نگه می‌دارد؛ سپس هسته همراه پروکسی داخلی و TUN را می‌سازد. DNS رمزنگاری‌شده از مسیر SSH عبور می‌کند و کانفیگ موقت پس از Disconnect حذف می‌شود.
Nivan saves the previous adapter state before the first DNS change. Restore is always available even if a DNS test fails.	نیوان پیش از اولین تغییر DNS وضعیت کارت شبکه را ذخیره می‌کند؛ حتی اگر تست شکست بخورد امکان Restore وجود دارد.
Nivan verifies the ZIP against the SHA-256 value in the manifest and never installs it automatically. A public release should additionally sign the manifest and EXE.	نیوان SHA-256 فایل ZIP را بررسی می‌کند و هرگز آن را خودکار نصب نمی‌کند. نسخه عمومی باید Manifest و EXE امضاشده داشته باشد.
Choose Browser only, Selected applications, Whole Windows, or System Proxy on Home. Whole Windows enables TUN and System Proxy together for NekoRay-compatible behavior.	در Home حالت Browser only، برنامه‌های انتخابی، کل ویندوز یا System Proxy را انتخاب کنید. حالت کل ویندوز برای سازگاری با NekoRay، TUN و System Proxy را هم‌زمان روشن می‌کند.
Core has not been checked yet.	هسته هنوز بررسی نشده است.
Auto-reconnect protected	محافظت با اتصال مجدد خودکار
Locked to the original working SSH flow	مطابق روند اصلی و سالم SSH
SOCKS5 endpoint	آدرس SOCKS5
LOCAL PROXY	پروکسی محلی
SSH ROUTING	ROUTING SSH
SSH SERVER	سرور SSH
SSH, V2Ray config, or subscription	SSH، کانفیگ V2Ray یا اشتراک
Subscription	اشتراک
Browse	انتخاب
client.config supplied with the official core	فایل client.config همراه هسته رسمی
ConsoleClient.exe	ConsoleClient.exe
DNS	DNS
example.com:443	example.com:443
Example: game.exe; accounting.exe	مثال: game.exe; accounting.exe
Example: ir; company.local	مثال: ir; company.local
Example: Main servers	مثال: سرورهای اصلی
HTTP	HTTP
HTTPS	HTTPS
IDLE	آماده
Nivan Shield	نیوان شیلد
SOCKS5	SOCKS5
SOCKS5  •  127.0.0.1:1080	SOCKS5  •  127.0.0.1:1080
SSH	SSH
Vendor manifest containing version, download_url, sha256, and notes	Manifest سازنده شامل version، download_url، sha256 و توضیحات
Version 6.0.5  •  Open-source multi-provider client	نسخه ۶.۰.۵  •  کلاینت متن‌باز چندسرویسی
Connect or disconnect	اتصال یا قطع اتصال
Connect active profile	اتصال پروفایل فعال
Disconnect current connection	قطع اتصال فعلی
Provider connected. Verifying browser and DNS routing...	سرویس متصل شد؛ مسیر مرورگر و DNS در حال بررسی است...
Browser and DNS routing verified	مسیر مرورگر و DNS تأیید شد
Browser proxy and routed DNS are ready	پروکسی مرورگر و DNS مسیریابی‌شده آماده‌اند
Integrated routing stopped before verification completed.	مسیریابی داخلی پیش از پایان بررسی متوقف شد.
Making access to the free and open internet easier.	تسهیل دسترسی آدم‌ها به دنیای آزاد اینترنت
Subscriptions	اشتراک‌ها
Add & import now	افزودن و ورود فوری
Add a link once. Its compatible servers are downloaded and imported immediately; no separate refresh or save step is required.	لینک را یک‌بار اضافه کنید؛ سرورهای سازگار همان لحظه دانلود و وارد می‌شوند و نیازی به Refresh یا Save جداگانه نیست.
Keyboard shortcuts	میانبرهای صفحه‌کلید
Reset shortcuts	بازگردانی میانبرها
Click a field and type a shortcut such as Ctrl+I. Shortcuts must be unique.	روی هر کادر کلیک و میانبری مانند Ctrl+I وارد کنید؛ میانبرها باید متفاوت باشند.
Import config file	ورود فایل کانفیگ
Import QR image	ورود تصویر QR
New connection	اتصال جدید
Select all profiles	انتخاب همه پروفایل‌ها
Delete profiles	حذف پروفایل‌ها
Duplicate profile	کپی پروفایل
SSH account	حساب SSH
V2Ray config or QR	کانفیگ یا QR وی‌تو‌ری
V2Ray subscription	اشتراک V2Ray
External SOCKS / HTTP proxy	پروکسی خارجی SOCKS / HTTP
Psiphon provider	سرویس Psiphon
Offline QR import — shortcut is configurable in Settings	ورود آفلاین QR — میانبر از تنظیمات قابل تغییر است
VLESS	VLESS
VMess / VLESS / Trojan / Shadowsocks	VMess / VLESS / Trojan / Shadowsocks
ws  •  tls	ws  •  tls
At least one connection profile must remain.	حداقل یک پروفایل اتصال باید باقی بماند.
CANCELLED	لغوشده
Checking for updates...	در حال بررسی به‌روزرسانی...
Checking provider and network path.	در حال بررسی سرویس و مسیر شبکه.
Checking sing-box executable...	در حال بررسی فایل sing-box...
Clear the current activity log?	گزارش فعلی پاک شود؟
Connect a profile before testing the VPN path.	پیش از تست مسیر VPN یک پروفایل را متصل کنید.
Custom connection cores are disabled in secure mode. Nivan runs only the reviewed bundled core.	هسته سفارشی در حالت امن غیرفعال است و فقط هسته بررسی‌شده همراه نیوان اجرا می‌شود.
Custom DNS saved. Use Test selected before applying it.	DNS سفارشی ذخیره شد؛ پیش از اعمال آن را آزمایش کنید.
Diagnosis failed	عیب‌یابی ناموفق بود
Disconnect before deleting the active profile.	پیش از حذف پروفایل فعال، اتصال را قطع کنید.
Disconnect before switching the active connection.	پیش از تغییر اتصال فعال، اتصال فعلی را قطع کنید.
Disconnect before switching the active profile.	پیش از تغییر پروفایل فعال، اتصال را قطع کنید.
Disconnect before switching to Psiphon.	پیش از رفتن به Psiphon اتصال فعلی را قطع کنید.
Disconnect the current connection before switching or reconnecting a profile.	پیش از تغییر یا اتصال مجدد پروفایل، اتصال فعلی را قطع کنید.
The current connection did not stop completely. Try Disconnect again before switching profiles.	اتصال فعلی کامل متوقف نشد. پیش از تغییر پروفایل دوباره قطع اتصال را امتحان کنید.
One or more connection providers could not be stopped.	توقف یک یا چند سرویس اتصال ممکن نشد.
Disconnect the current connection before using Smart Connect.	پیش از Smart Connect اتصال فعلی را قطع کنید.
Disconnect the current V2Ray connection before switching to SSH.	پیش از رفتن به SSH اتصال V2Ray را قطع کنید.
Downloading subscription through HTTPS...	در حال دانلود اشتراک از HTTPS...
Downloading verified update...	در حال دانلود به‌روزرسانی تأییدشده...
Enter a valid HTTP or HTTPS subscription URL.	یک لینک معتبر HTTP یا HTTPS برای اشتراک وارد کنید.
Enter the vendor's HTTPS update manifest URL first.	ابتدا لینک HTTPS مربوط به Manifest به‌روزرسانی را وارد کنید.
Enter valid IPv4 addresses for the custom DNS.	آدرس‌های معتبر IPv4 برای DNS سفارشی وارد کنید.
External proxy added. Test it before sending sensitive traffic.	پروکسی خارجی اضافه شد؛ پیش از ارسال اطلاعات حساس آن را آزمایش کنید.
FAILED	ناموفق
Finding best...	در حال یافتن بهترین اتصال...
Host-key management is available only for SSH profiles.	مدیریت Host Key فقط برای پروفایل SSH در دسترس است.
Integrated routing settings saved. Changes apply on the next SSH connection.	تنظیمات Routing ذخیره شد و در اتصال بعدی SSH اعمال می‌شود.
No managed subscriptions have been saved.	اشتراک مدیریت‌شده‌ای ذخیره نشده است.
No profile selected.	پروفایلی انتخاب نشده است.
No result was saved.	نتیجه‌ای ذخیره نشد.
No SSH profile is available. Open Servers and create an SSH profile first.	پروفایل SSH موجود نیست؛ ابتدا از بخش Connections یک پروفایل SSH بسازید.
No usable profile was found. Save the SSH password/private key or import a proxy config first.	پروفایل قابل‌استفاده پیدا نشد؛ ابتدا رمز یا کلید SSH را ذخیره یا کانفیگ پروکسی وارد کنید.
None of the usable profiles responded to the server test.	هیچ‌کدام از پروفایل‌های قابل‌استفاده به تست سرور پاسخ ندادند.
Official Psiphon files verified. The provider is ready.	فایل‌های رسمی Psiphon تأیید شدند و سرویس آماده است.
Preferences saved.	تنظیمات ذخیره شد.
Previous Windows DNS settings restored.	تنظیمات قبلی DNS ویندوز بازگردانی شد.
Profile saved.	پروفایل ذخیره شد.
Psiphon settings	تنظیمات Psiphon
Psiphon settings saved.	تنظیمات Psiphon ذخیره شد.
Running connection diagnosis...	در حال عیب‌یابی اتصال...
Saving...	در حال ذخیره...
SING-BOX CORE	هسته SING-BOX
sing-box settings saved.	تنظیمات sing-box ذخیره شد.
Stored SSH host key removed.	Host Key ذخیره‌شده SSH حذف شد.
Test cancelled	آزمایش لغو شد
Testing DNS providers...	در حال آزمایش سرویس‌های DNS...
Testing profiles in parallel...	در حال آزمایش هم‌زمان پروفایل‌ها...
Testing...	در حال آزمایش...
The active profile does not use an SSH password.	پروفایل فعال از رمز SSH استفاده نمی‌کند.
The connection is active. Disconnect and exit Nivan Shield?	اتصال فعال است؛ اتصال قطع و از نیوان خارج شود؟
The connection test was stopped.	آزمایش اتصال متوقف شد.
The encrypted credential for this imported profile is missing. Import the config again before duplicating it.	اطلاعات رمزنگاری‌شده این پروفایل موجود نیست؛ پیش از کپی‌کردن، کانفیگ را دوباره وارد کنید.
Update download cancelled.	دانلود به‌روزرسانی لغو شد.
V2Ray manager	مدیریت V2Ray
Verifying official Psiphon signature and pinned fingerprint...	در حال بررسی امضای رسمی و fingerprint هسته Psiphon...
Verifying the Windows publisher signature...	در حال بررسی امضای ناشر ویندوز...
VPN path test failed	تست مسیر VPN ناموفق بود
The connection manager is still running.	مدیریت اتصال همچنان در حال اجراست.
Restore normal internet	بازگردانی اینترنت عادی
Restoring...	در حال بازگردانی...
Saved V2Ray profile	پروفایل ذخیره‌شده V2Ray
Only V2Ray configs appear here.	فقط کانفیگ‌های V2Ray در اینجا نمایش داده می‌شوند.
Use this profile	استفاده از این پروفایل
Connect now	اتصال اکنون
Active profile	پروفایل فعال
Re-import below	ورود مجدد در بخش پایین
V2Ray profiles	پروفایل‌های V2Ray
SSH profiles	پروفایل‌های SSH
Only imported V2Ray configs are shown here	اینجا فقط کانفیگ‌های واردشده V2Ray نمایش داده می‌شوند
Only SSH accounts are shown here	اینجا فقط حساب‌های SSH نمایش داده می‌شوند
No V2Ray profile yet. Add a subscription above or import a config on this page.	هنوز پروفایل V2Ray ندارید؛ از بالا اشتراک اضافه کنید یا در همین صفحه کانفیگ وارد کنید.
Normal internet restored	اینترنت عادی بازگردانی شد
Reset incomplete	بازگردانی ناقص بود
Normal Windows internet settings restored	تنظیمات عادی اینترنت ویندوز بازگردانی شد
N I V A N	N I V A N
S E S S I O N   U P T I M E	مدت اتصال
Disconnect the current connection before switching to V2Ray.	پیش از رفتن به V2Ray اتصال فعلی را قطع کنید.
No usable V2Ray profile is available. Paste the original config or refresh its subscription.	پروفایل قابل‌استفاده V2Ray موجود نیست؛ کانفیگ اصلی را دوباره وارد یا اشتراک آن را تازه‌سازی کنید.
Disconnect Nivan, clear its Windows LAN proxy values, restore DNS changed by Nivan, and flush the DNS cache?	اتصال نیوان قطع، مقادیر LAN Proxy متعلق به آن پاک، DNS تغییریافته بازگردانی و حافظه DNS خالی شود؟
Saved profiles and account details will not be deleted.	پروفایل‌ها و اطلاعات حساب ذخیره‌شده حذف نمی‌شوند.
Nivan profiles and saved account details were kept.	پروفایل‌ها و اطلاعات حساب ذخیره‌شده نیوان حفظ شدند.
The encrypted credential is missing. Paste the original config again or refresh its subscription; Nivan will repair this saved profile.	اطلاعات رمزنگاری‌شده موجود نیست؛ کانفیگ اصلی را دوباره وارد یا اشتراک را تازه‌سازی کنید تا نیوان همین پروفایل را ترمیم کند.
The encrypted credential is empty. Import this config again.	اطلاعات رمزنگاری‌شده خالی است؛ کانفیگ را دوباره وارد کنید.
Import QR image...	ورود تصویر QR...
Offline QR import (Ctrl+Shift+I)	ورود آفلاین QR (Ctrl+Shift+I)
Shortcuts: Ctrl+A select all  •  Delete remove  •  Ctrl+D duplicate  •  Ctrl+N new SSH	میانبرها: Ctrl+A انتخاب همه  •  Delete حذف  •  Ctrl+D کپی  •  Ctrl+N ساخت SSH
Select all	انتخاب همه
Delete selected	حذف انتخاب‌شده‌ها
Reading QR...	در حال خواندن QR...
QR import	ورود QR
Unsupported QR content	محتوای QR پشتیبانی نمی‌شود
No readable QR code was found in this image.	کد QR خوانایی در این تصویر پیدا نشد.
The QR code was readable, but it does not contain a supported V2Ray share link or an HTTP/HTTPS subscription.	کد QR خوانده شد، اما شامل کانفیگ V2Ray پشتیبانی‌شده یا اشتراک HTTP/HTTPS نیست.
Delete selected profiles	حذف پروفایل‌های انتخاب‌شده
Use Delete to remove the selected profiles together.	برای حذف گروهی پروفایل‌های انتخاب‌شده از Delete استفاده کنید.
";
    }
}
