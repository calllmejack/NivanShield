using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Nivan.Shield.Core;
using Nivan.Shield.Services;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Nivan.Shield.UI
{
    public sealed class MainController : IDisposable
    {
        private static readonly Version CurrentVersion = new Version(6, 0, 5);
        private readonly Window _window;
        private readonly AppPaths _paths;
        private readonly AppLogger _logger;
        private readonly SettingsService _settingsService;
        private readonly CredentialService _credentials;
        private readonly ProxySecretService _proxySecrets;
        private readonly SubscriptionSecretService _subscriptionSecrets;
        private readonly V2RayConfigImporter _configImporter;
        private readonly SubscriptionService _subscriptions;
        private readonly NekoRayService _nekoRay;
        private readonly SystemProxyService _systemProxy;
        private readonly CrashRecoveryService _crashRecovery;
        private readonly ConnectionManager _manager;
        private readonly ProfileHealthService _profileHealth;
        private readonly ConnectionQualityService _connectionQuality;
        private readonly SmartConnectService _smartConnect;
        private readonly AppUpdateService _appUpdates;
        private readonly BinaryIntegrityService _integrity;
        private readonly DnsService _dns;
        private readonly BrowserProxyService _browserProxy;
        private readonly ExternalProxyService _externalProxy;
        private readonly ConnectionDiagnosticsService _diagnostics;
        private readonly LocalizationService _localization;
        private readonly QrConfigDecoderService _qrDecoder;
        private IList<BrowserChoice> _browserChoices;
        private IList<DnsProviderInfo> _dnsProviders;
        private bool _dnsBusy;
        private readonly DispatcherTimer _timer;
        private readonly ObservableCollection<ProfileRowViewModel> _profileRows;
        private readonly ObservableCollection<SubscriptionRowViewModel> _subscriptionRows;
        private AppSettings _settings;
        private Forms.NotifyIcon _trayIcon;
        private Forms.ToolStripItem _trayOpenItem;
        private Forms.ToolStripItem _trayConnectItem;
        private Forms.ToolStripItem _trayDisconnectItem;
        private Forms.ToolStripItem _trayExitItem;
        private DateTime? _connectedAt;
        private bool _allowExit;
        private bool _refreshingProfiles;
        private bool _refreshingHomeProfile;
        private bool _testingProfiles;
        private bool _qualityTestRunning;
        private CancellationTokenSource _qualityTestCancellation;
        private DateTime? _lastAutoHealthAt;
        private readonly HashSet<string> _failoverAttemptedProfileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private CancellationTokenSource _failoverDelayCancellation;
        private bool _smartConnectRunning;
        private bool _failoverInProgress;
        private bool _manualDisconnectRequested;
        private bool _subscriptionBusy;
        private bool _refreshingV2RayProfiles;
        private bool _emergencyResetRunning;
        private bool _powerActionRunning;
        private bool _profileSwitchRunning;
        private string _profileProviderScope = "All";
        private bool _updateBusy;
        private AppUpdateInfo _availableUpdate;
        private CancellationTokenSource _updateDownloadCancellation;
        private bool _disposed;

        public MainController(Window window)
        {
            _window = window;
            _paths = new AppPaths();
            _logger = new AppLogger(_paths.LogPath);
            _settingsService = new SettingsService(_paths.SettingsPath, _logger);
            _settings = _settingsService.Load();

            _credentials = new CredentialService(_paths, _logger);
            _proxySecrets = new ProxySecretService(_paths, _logger);
            _subscriptionSecrets = new SubscriptionSecretService(_paths, _logger);
            _integrity = new BinaryIntegrityService();
            _dns = new DnsService(_paths, _logger);
            _browserProxy = new BrowserProxyService(_paths, _integrity);
            _externalProxy = new ExternalProxyService(_proxySecrets);
            _configImporter = new V2RayConfigImporter();
            _subscriptions = new SubscriptionService(_logger);
            ConnectionProfile firstSsh = _settings.Profiles.Items.FirstOrDefault(
                delegate(ConnectionProfile profile) { return profile.IsSsh; }
            );
            if (firstSsh != null) _credentials.MigrateLegacyCredentialIfNeeded(firstSsh.Id);
            foreach (ConnectionProfile profile in _settings.Profiles.Items)
            {
                if (profile.IsSsh && String.Equals(profile.Tunnel.AuthMode, "Password", StringComparison.OrdinalIgnoreCase))
                    profile.Tunnel.UseSavedPassword = _credentials.Exists(profile.Id);
            }
            if (_settings.SingBox.UseBundledCore && File.Exists(_paths.BundledNekoCorePath))
                _settings.SingBox.ExecutablePath = _paths.BundledNekoCorePath;
            else if (String.IsNullOrWhiteSpace(_settings.SingBox.ExecutablePath)
                && File.Exists(_paths.BundledSingBoxPath))
                _settings.SingBox.ExecutablePath = _paths.BundledSingBoxPath;
            if (_settings.NekoRay.UseBundledPortable && File.Exists(_paths.BundledNekoCorePath))
                _settings.NekoRay.ExecutablePath = _paths.BundledNekoCorePath;
            if (String.IsNullOrWhiteSpace(_settings.Psiphon.ExecutablePath) && File.Exists(_paths.BundledPsiphonPath))
                _settings.Psiphon.ExecutablePath = _paths.BundledPsiphonPath;
            if (String.IsNullOrWhiteSpace(_settings.Psiphon.ConfigPath) && File.Exists(_paths.BundledPsiphonConfigPath))
                _settings.Psiphon.ConfigPath = _paths.BundledPsiphonConfigPath;

            _nekoRay = new NekoRayService(_logger, _paths, _integrity);
            _profileHealth = new ProfileHealthService(_logger);
            _connectionQuality = new ConnectionQualityService(_logger);
            _smartConnect = new SmartConnectService(_profileHealth, _logger);
            _appUpdates = new AppUpdateService(_logger);
            _profileRows = new ObservableCollection<ProfileRowViewModel>();
            _subscriptionRows = new ObservableCollection<SubscriptionRowViewModel>();
            _systemProxy = new SystemProxyService(_logger);
            _diagnostics = new ConnectionDiagnosticsService(_systemProxy);
            _localization = new LocalizationService();
            _qrDecoder = new QrConfigDecoderService();
            _crashRecovery = new CrashRecoveryService(_paths, _systemProxy, _dns, _logger);
            SshConnectionProvider ssh = new SshConnectionProvider(_logger, _paths, _credentials);
            SingBoxConnectionProvider singBox = new SingBoxConnectionProvider(
                _logger,
                _paths,
                _proxySecrets,
                new SingBoxConfigBuilder(),
                _integrity
            );
            XrayConnectionProvider xray = new XrayConnectionProvider(
                _logger,
                _paths,
                _proxySecrets,
                new XrayConfigBuilder(),
                _integrity
            );
            PsiphonConnectionProvider psiphon = new PsiphonConnectionProvider(_logger, _paths, _integrity);
            ConnectionProviderRouter router = new ConnectionProviderRouter(ssh, singBox, xray, psiphon);
            _manager = new ConnectionManager(router, _nekoRay, _systemProxy, _dns, _logger);

            _manager.StateChanged += OnConnectionStateChanged;
            _logger.LineWritten += OnLogLineWritten;
            _timer = new DispatcherTimer(DispatcherPriority.Background, _window.Dispatcher);
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += OnTimerTick;
        }

        private ConnectionProfile ActiveProfile
        {
            get
            {
                ConnectionProfile profile = _settings.Profiles.Find(_settings.Profiles.ActiveProfileId);
                if (profile != null) return profile;
                profile = _settings.Profiles.Items[0];
                _settings.Profiles.ActiveProfileId = profile.Id;
                if (profile.IsSsh) _settings.Tunnel = profile.Tunnel;
                return profile;
            }
        }

        private string ActiveProfileId { get { return ActiveProfile.Id; } }

        private ConnectionProfile SelectedProfile
        {
            get
            {
                ListBox list = Find<ListBox>("ProfileList");
                if (list.SelectedItems.Count != 1) return null;
                ProfileRowViewModel row = list.SelectedItem as ProfileRowViewModel;
                return row == null ? null : row.Profile;
            }
        }

        private List<ConnectionProfile> SelectedProfiles
        {
            get
            {
                return Find<ListBox>("ProfileList").SelectedItems
                    .Cast<ProfileRowViewModel>()
                    .Where(delegate(ProfileRowViewModel row) { return row != null && row.Profile != null; })
                    .Select(delegate(ProfileRowViewModel row) { return row.Profile; })
                    .ToList();
            }
        }

        private SubscriptionEntry SelectedSubscription
        {
            get
            {
                SubscriptionRowViewModel row = Find<ListBox>("SubscriptionList").SelectedItem as SubscriptionRowViewModel;
                return row == null ? null : row.Subscription;
            }
        }

        public void Initialize()
        {
            ValidateControls();
            _crashRecovery.BeginSession(_settings.Network, _settings.Dns);
            LoadControlsFromSettings();
            WireEvents();
            CreateTrayIcon();
            ShowPage("Dashboard");
            UpdateConnectionVisual(ConnectionState.Offline, "Ready to connect");
            UpdateNekoVisual();
            RefreshHealthDisplay();
            BeginBackgroundMaintenance();
            RefreshLogs();
            ApplyLanguage();
            _timer.Start();
            _settingsService.Save(_settings);
            _logger.Info("Nivan Shield 6.0.5 started in secure provider mode.");

            if (_settings.App.StartMinimized)
            {
                _window.ShowInTaskbar = false;
                _window.WindowState = WindowState.Minimized;
                _window.Hide();
            }
        }

        private T Find<T>(string name) where T : class
        {
            T control = _window.FindName(name) as T;
            if (control == null) throw new InvalidOperationException("UI control was not found: " + name);
            return control;
        }

        private void ValidateControls()
        {
            string[] names = new string[]
            {
                "NavDashboard", "NavHealth", "NavProfiles", "NavConnection", "NavV2Ray", "NavPsiphon", "NavDns", "NavNekoRay", "NavLogs", "NavAbout",
                "DashboardPage", "HealthPage", "ProfilesPage", "ConnectionPage", "V2RayPage", "PsiphonPage", "DnsPage", "NekoRayPage", "LogsPage", "AboutPage",
                "PowerButton", "ConnectButton", "DisconnectButton", "SmartConnectButton", "TestServerButton", "CopyProxyButton",
                "LanguageInput", "HomeProfileInput", "HomeProfileDetailText", "HomeManageProfilesButton", "HomeDnsButton", "HomeDnsStatusText",
                "EmergencyNetworkResetButton",
                "OpenSshQuickButton", "OpenConfigQuickButton", "OpenSubscriptionQuickButton",
                "OpenPsiphonQuickButton", "RoutingModeInput", "RoutingModeDetailText", "SelectedAppsPanel",
                "SelectedAppsInput", "BrowseSelectedAppButton", "ClearSelectedAppsButton", "BrowserInput",
                "OpenProtectedBrowserButton", "RunDiagnosticsButton", "OpenDiagnosticsButton",
                "DiagnosticStatusText", "DiagnosticDetailText",
                "V2RayConnectButton", "V2RayDisconnectButton", "OpenServersButton",
                "V2RayConnectionStatusText", "V2RayProfileInput", "V2RayProfileStatusText", "UseV2RayProfileButton",
                "OpenRoutingSettingsButton", "OpenActivityLogsButton",
                "SshConnectButton", "SshDisconnectButton",
                "DashboardActiveProfileText", "DashboardServerKindText", "DashboardAuxTitleText",
                "DashboardHealthSummaryText", "DashboardHealthDetailText", "OpenHealthButton",
                "QuickQualityTestButton", "FullQualityTestButton", "CancelQualityTestButton",
                "HealthScoreText", "HealthOverallText", "HealthDetailText", "HealthProgressBar",
                "HealthProgressStageText", "HealthProgressDetailText", "HealthServerLatencyText",
                "HealthTunnelLatencyText", "HealthJitterText", "HealthFailureRateText",
                "HealthDownloadText", "HealthUploadText", "HealthHistoryBox",
                "HostInput", "PortInput", "UserInput", "SocksPortInput",
                "AuthModeInput", "KeyPathInput", "PasswordInput", "AutoLoginCheck", "PasswordStatusText",
                "KeepAliveInput", "KeepAliveCountInput", "ReconnectDelayInput", "AutoReconnectCheck",
                "ProfileSearchInput", "ProfileCategoryFilter", "ProfileSortInput", "ProfileList", "ProfileShortcutHintText",
                "ProfilesPageTitleText", "ProfilesPageSubtitleText",
                "NewProfileButton", "SelectAllProfilesButton", "DuplicateProfileButton", "DeleteProfileButton", "TestAllProfilesButton",
                "SelectedProfileStatusText", "ProfileNameInput", "ProfileCategoryInput", "ProfileFavoriteCheck",
                "ProfileEngineText", "SshProfileEditorPanel", "ProxyProfileEditorPanel",
                "ProfileHostInput", "ProfilePortInput", "ProfileUserInput", "ProfileSocksPortInput",
                "ProxyProtocolText", "ProxyServerText", "ProxyTransportText", "ProxyLocalPortInput",
                "ProxyCredentialStatusText",
                "ProfileTestDetailText", "SaveProfileButton", "SetActiveProfileButton",
                "TestSelectedProfileButton", "EditAdvancedProfileButton",
                "SingBoxPathInput", "BrowseSingBoxButton", "TestSingBoxButton", "SingBoxStatusText",
                "UseBundledCoreCheck", "SingBoxAutoReconnectCheck", "SingBoxReconnectDelayInput", "SaveSingBoxButton",
                "ConfigImportInput", "ImportCategoryInput", "PasteConfigsButton", "ImportConfigFileButton", "ImportQrConfigButton",
                "ImportConfigsButton", "ImportResultText", "SubscriptionNameInput", "SubscriptionUrlInput",
                "SubscriptionCategoryInput", "SubscriptionAutoUpdateCheck", "DownloadSubscriptionButton",
                "SubscriptionList", "SubscriptionStatusText", "RemoveSubscriptionButton",
                "RefreshSelectedSubscriptionButton", "RefreshAllSubscriptionsButton",
                "ExternalProxyNameInput", "ExternalProxyProtocolInput", "ExternalProxyHostInput",
                "ExternalProxyPortInput", "ExternalProxyUsernameInput", "ExternalProxyPasswordInput",
                "ExternalProxyStatusText", "AddExternalProxyButton",
                "PsiphonCorePathInput", "BrowsePsiphonCoreButton", "PsiphonConfigPathInput",
                "BrowsePsiphonConfigButton", "PsiphonSocksPortInput", "PsiphonHttpPortInput",
                "PsiphonRegionInput", "PsiphonAutoReconnectCheck", "PsiphonReconnectDelayInput",
                "PsiphonStatusText", "PsiphonHashText", "TestPsiphonButton", "SavePsiphonButton",
                "CreatePsiphonProfileButton",
                "DnsProviderInput", "ApplyDnsButton", "TestDnsButton", "TestAllDnsButton",
                "RestoreDnsButton", "DnsStatusText", "DnsResultsBox", "CustomDnsNameInput",
                "CustomDnsPrimaryInput", "CustomDnsSecondaryInput", "SaveCustomDnsButton",
                "DnsRestoreOnDisconnectCheck", "DnsRestoreAfterCrashCheck",
                "NekoEnabledCheck", "NekoAutoStartCheck", "NekoSystemProxyCheck", "NekoTunCheck",
                "NekoCloseCheck", "NekoDelayInput", "NekoMixedPortInput", "LogBox", "DashboardLogBox",
                "MinimizeToTrayCheck", "StartMinimizedCheck", "ConfirmExitCheck",
                "EnableLanProxyOnConnectCheck", "DisableLanProxyOnDisconnectCheck",
                "AutoHealthCheckAfterConnectCheck", "HealthLatencySamplesInput",
                "QuickDownloadSizeInput", "FullDownloadSizeInput", "FullUploadSizeInput",
                "AutoFailoverCheck", "PreferFavoritesCheck", "FailoverDelayInput", "FailoverMaxAttemptsInput",
                "RecoverProxyAfterCrashCheck", "ProxyNetworkLockCheck", "SplitTunnelingCheck",
                "ProxyBypassListInput", "TunBypassProcessesInput", "TunBypassDomainsInput", "TunBypassCidrsInput",
                "ShortcutImportConfigInput", "ShortcutImportQrInput", "ShortcutSelectAllInput",
                "ShortcutDeleteInput", "ShortcutDuplicateInput", "ShortcutNewConnectionInput", "ResetShortcutsButton",
                "AppLayoutGrid", "SidebarColumn", "ContentColumn", "SidebarPanel", "ContentPanel",
                "CheckUpdatesOnStartupCheck", "UpdateManifestUrlInput", "CheckUpdatesButton",
                "DownloadUpdateButton", "OpenUpdateFolderButton", "UpdateStatusText", "UpdateProgressBar"
            };
            foreach (string name in names)
            {
                if (_window.FindName(name) == null)
                    throw new InvalidOperationException("UI control was not found: " + name);
            }
        }

        private void WireEvents()
        {
            Find<Button>("NavDashboard").Click += delegate { ShowPage("Dashboard"); };
            Find<Button>("NavHealth").Click += delegate { ShowPage("Health"); };
            Find<Button>("NavProfiles").Click += delegate { OpenProfileManager("All"); };
            Find<Button>("NavConnection").Click += delegate
            {
                if (ActiveProfile.IsSsh) ShowPage("Connection");
                else if (ActiveProfile.IsPsiphon) ShowPage("Psiphon");
                else
                {
                    ShowInfo("The active profile uses " + ActiveProfile.ProtocolLabel + ". Open V2Ray & sing-box to manage imported configs.");
                    ShowPage("V2Ray");
                }
            };
            Find<Button>("NavV2Ray").Click += delegate { OpenV2RayQuickStart(false); };
            Find<Button>("NavPsiphon").Click += delegate { OpenPsiphonQuickStart(); };
            Find<Button>("NavDns").Click += delegate { ShowPage("Dns"); };
            Find<Button>("NavNekoRay").Click += delegate { ShowPage("NekoRay"); };
            Find<Button>("NavLogs").Click += delegate { ShowPage("Logs"); };
            Find<Button>("NavAbout").Click += delegate { ShowPage("About"); };
            Find<ComboBox>("LanguageInput").SelectionChanged += delegate
            {
                string language = SelectedTag(Find<ComboBox>("LanguageInput"));
                if (!String.Equals(language, "fa", StringComparison.OrdinalIgnoreCase)) language = "en";
                _settings.App.Language = language;
                _settingsService.Save(_settings);
                ApplyLanguage();
            };
            Find<Button>("HomeManageProfilesButton").Click += delegate { OpenProfileManager("All"); };
            Find<Button>("HomeDnsButton").Click += delegate { ShowPage("Dns"); };
            Find<ComboBox>("HomeProfileInput").SelectionChanged += delegate
            {
                ActivateHomeProfileSelection();
            };
            Find<Button>("OpenFullLogsButton").Click += delegate { ShowPage("Logs"); };
            Find<Button>("OpenHealthButton").Click += delegate { ShowPage("Health"); };
            Find<Button>("OpenSshQuickButton").Click += delegate { OpenSshQuickStart(); };
            Find<Button>("OpenConfigQuickButton").Click += delegate { OpenV2RayQuickStart(false); };
            Find<Button>("OpenSubscriptionQuickButton").Click += delegate
            {
                OpenV2RayQuickStart(false);
                Find<TextBox>("SubscriptionUrlInput").Focus();
            };
            Find<Button>("OpenServersButton").Click += delegate { OpenProfileManager("V2Ray"); };
            Find<Button>("OpenPsiphonQuickButton").Click += delegate { OpenPsiphonQuickStart(); };
            Find<ComboBox>("V2RayProfileInput").SelectionChanged += delegate { SelectV2RayProfileFromInput(); };
            Find<Button>("UseV2RayProfileButton").Click += async delegate { await UseSelectedV2RayProfileAsync(); };
            Find<ComboBox>("V2RayProfileInput").MouseDoubleClick += async delegate
            {
                await UseSelectedV2RayProfileAsync();
            };
            Find<Button>("EmergencyNetworkResetButton").Click += async delegate { await EmergencyNetworkResetAsync(); };
            Find<Button>("PowerButton").Click += async delegate { await TogglePowerAsync(); };
            Find<Button>("V2RayConnectButton").Click += async delegate { await ConnectAsync(); };
            Find<Button>("V2RayDisconnectButton").Click += async delegate { await DisconnectAsync(); };
            Find<Button>("SshConnectButton").Click += async delegate { await ConnectAsync(); };
            Find<Button>("SshDisconnectButton").Click += async delegate { await DisconnectAsync(); };
            Find<Button>("OpenRoutingSettingsButton").Click += delegate { ShowPage("NekoRay"); };
            Find<Button>("OpenActivityLogsButton").Click += delegate { ShowPage("Logs"); };

            Find<Button>("ConnectButton").Click += async delegate { await ConnectAsync(); };
            Find<Button>("SmartConnectButton").Click += async delegate { await SmartConnectAsync(); };
            Find<Button>("DisconnectButton").Click += async delegate { await DisconnectAsync(); };
            Find<Button>("TestServerButton").Click += async delegate { await TestServerAsync(); };
            Find<Button>("RunDiagnosticsButton").Click += async delegate { await RunDiagnosticsAsync(); };
            Find<Button>("OpenDiagnosticsButton").Click += async delegate { await RunDiagnosticsAsync(); };
            Find<Button>("TestServerConnectionButton").Click += async delegate { await TestServerAsync(); };
            Find<Button>("QuickQualityTestButton").Click += async delegate
            {
                await RunQualityTestAsync(ConnectionQualityTestKind.Quick, true);
            };
            Find<Button>("FullQualityTestButton").Click += async delegate
            {
                await RunQualityTestAsync(ConnectionQualityTestKind.Full, true);
            };
            Find<Button>("CancelQualityTestButton").Click += delegate { CancelQualityTest(); };
            Find<Button>("CopyProxyButton").Click += delegate
            {
                Clipboard.SetText("socks5://127.0.0.1:" + ActiveProfile.LocalSocksPort);
                _logger.Info("SOCKS5 endpoint copied to the clipboard.");
            };
            Find<ComboBox>("RoutingModeInput").SelectionChanged += delegate
            {
                if (_window.IsLoaded) SaveRoutingModeControls(false);
                UpdateRoutingModeUi();
            };
            Find<Button>("BrowseSelectedAppButton").Click += delegate { AddSelectedApplication(); };
            Find<Button>("ClearSelectedAppsButton").Click += delegate
            {
                Find<TextBox>("SelectedAppsInput").Clear();
                SaveRoutingModeControls(false);
            };
            Find<Button>("OpenProtectedBrowserButton").Click += delegate { OpenProtectedBrowser(); };

            Find<Button>("SaveConnectionButton").Click += delegate
            {
                if (SaveConnectionControls(true)) ShowInfo("Connection settings saved for " + ActiveProfile.Name + ".");
            };
            Find<Button>("ClearSavedPasswordButton").Click += delegate { ClearSavedPassword(); };
            Find<Button>("BrowseKeyButton").Click += delegate { BrowsePrivateKey(); };
            Find<Button>("ForgetHostKeyButton").Click += async delegate { await ForgetHostKeyAsync(); };

            Find<TextBox>("ProfileSearchInput").TextChanged += delegate
            {
                if (!_refreshingProfiles) RefreshProfileList(null);
            };
            Find<ComboBox>("ProfileCategoryFilter").SelectionChanged += delegate
            {
                if (!_refreshingProfiles) RefreshProfileList(null);
            };
            Find<ComboBox>("ProfileSortInput").SelectionChanged += delegate
            {
                if (!_refreshingProfiles) RefreshProfileList(null);
            };
            Find<ListBox>("ProfileList").SelectionChanged += delegate
            {
                if (!_refreshingProfiles) LoadSelectedProfileEditor();
            };
            Find<ListBox>("ProfileList").MouseDoubleClick += async delegate
            {
                if (SelectedProfile != null) await UseSelectedProfileAsync();
            };
            Find<ListBox>("ProfileList").PreviewKeyDown += delegate(object sender, KeyEventArgs eventArgs)
            {
                ListBox list = (ListBox)sender;
                if (ShortcutMatches(_settings.Shortcuts.DeleteProfiles, eventArgs))
                {
                    DeleteSelectedProfiles();
                    eventArgs.Handled = true;
                }
                else if (ShortcutMatches(_settings.Shortcuts.SelectAllProfiles, eventArgs))
                {
                    list.SelectAll();
                    eventArgs.Handled = true;
                }
                else if (ShortcutMatches(_settings.Shortcuts.DuplicateProfile, eventArgs))
                {
                    DuplicateProfile();
                    eventArgs.Handled = true;
                }
                else if (ShortcutMatches(_settings.Shortcuts.NewConnection, eventArgs))
                {
                    ShowNewConnectionMenu();
                    eventArgs.Handled = true;
                }
            };
            Find<Button>("NewProfileButton").Click += delegate { ShowNewConnectionMenu(); };
            Find<Button>("SelectAllProfilesButton").Click += delegate { Find<ListBox>("ProfileList").SelectAll(); };
            Find<Button>("DuplicateProfileButton").Click += delegate { DuplicateProfile(); };
            Find<Button>("DeleteProfileButton").Click += delegate { DeleteSelectedProfiles(); };
            Find<Button>("SaveProfileButton").Click += delegate
            {
                if (SaveSelectedProfile(true)) ShowInfo("Profile saved.");
            };
            Find<Button>("SetActiveProfileButton").Click += async delegate { await UseSelectedProfileAsync(); };
            Find<Button>("EditAdvancedProfileButton").Click += delegate
            {
                ConnectionProfile selected = SelectedProfile;
                if (selected == null || !ActivateSelectedProfile(false)) return;
                ShowPage(selected.IsSsh ? "Connection" : selected.IsPsiphon ? "Psiphon" : "V2Ray");
            };
            Find<Button>("TestSelectedProfileButton").Click += async delegate { await TestSelectedProfileAsync(); };
            Find<Button>("TestAllProfilesButton").Click += async delegate { await TestAllProfilesAsync(); };

            Find<Button>("BrowseSingBoxButton").Click += delegate { BrowseSingBox(); };
            Find<CheckBox>("UseBundledCoreCheck").Click += delegate { ApplyPortableEngineChoiceUi(); };
            Find<Button>("TestSingBoxButton").Click += async delegate { await TestSingBoxAsync(); };
            Find<Button>("SaveSingBoxButton").Click += delegate
            {
                if (SaveSingBoxControls(true)) ShowInfo("sing-box settings saved.");
            };
            Find<Button>("PasteConfigsButton").Click += delegate
            {
                if (Clipboard.ContainsText()) Find<TextBox>("ConfigImportInput").Text = Clipboard.GetText();
            };
            Find<Button>("ImportConfigFileButton").Click += delegate { ImportConfigFile(); };
            Find<Button>("ImportQrConfigButton").Click += async delegate { await ImportQrImageAsync(); };
            Find<Button>("ImportConfigsButton").Click += delegate
            {
                ImportProxyText(
                    Find<TextBox>("ConfigImportInput").Text,
                    Find<TextBox>("ImportCategoryInput").Text
                );
            };
            Find<Button>("DownloadSubscriptionButton").Click += async delegate { await DownloadSubscriptionAsync(); };
            Find<ListBox>("SubscriptionList").SelectionChanged += delegate { UpdateSubscriptionSelection(); };
            Find<Button>("RefreshSelectedSubscriptionButton").Click += async delegate
            {
                await RefreshSelectedSubscriptionAsync();
            };
            Find<Button>("RefreshAllSubscriptionsButton").Click += async delegate
            {
                await RefreshAllSubscriptionsAsync(true);
            };
            Find<Button>("RemoveSubscriptionButton").Click += delegate { RemoveSelectedSubscription(); };
            Find<Button>("AddExternalProxyButton").Click += delegate { AddExternalProxy(); };

            Find<Button>("BrowsePsiphonCoreButton").Click += async delegate { await BrowsePsiphonCoreAsync(); };
            Find<Button>("BrowsePsiphonConfigButton").Click += delegate { BrowsePsiphonConfig(); };
            Find<Button>("TestPsiphonButton").Click += async delegate { await TestPsiphonAsync(); };
            Find<Button>("SavePsiphonButton").Click += delegate
            {
                if (SavePsiphonControls(true)) ShowInfo("Psiphon settings saved.");
            };
            Find<Button>("CreatePsiphonProfileButton").Click += delegate { CreatePsiphonProfile(); };

            Find<Button>("ApplyDnsButton").Click += async delegate { await ApplySelectedDnsAsync(); };
            Find<Button>("TestDnsButton").Click += async delegate { await TestSelectedDnsAsync(); };
            Find<Button>("TestAllDnsButton").Click += async delegate { await TestAllDnsAsync(); };
            Find<Button>("RestoreDnsButton").Click += async delegate { await RestoreDnsAsync(); };
            Find<Button>("SaveCustomDnsButton").Click += delegate { SaveCustomDns(); };
            Find<CheckBox>("DnsRestoreOnDisconnectCheck").Click += delegate { SaveDnsPreferences(); };
            Find<CheckBox>("DnsRestoreAfterCrashCheck").Click += delegate { SaveDnsPreferences(); };

            Find<Button>("SaveNekoButton").Click += delegate
            {
                if (SaveNekoControls())
                {
                    UpdateNekoVisual();
                    ShowInfo("Integrated routing settings saved. Changes apply on the next SSH connection.");
                }
            };
            Find<Button>("LaunchNekoButton").Click += async delegate { await LaunchNekoRayAsync(); };
            Find<Button>("StopNekoButton").Click += delegate
            {
                _manager.StopSshRouting();
                UpdateNekoVisual();
            };

            Find<Button>("SaveAppButton").Click += delegate
            {
                if (SaveAppControls()) ShowInfo("Preferences saved.");
            };
            Find<Button>("ResetShortcutsButton").Click += delegate
            {
                LoadShortcutControls(ShortcutSettings.CreateDefault());
            };
            Find<Button>("ExitAppButton").Click += async delegate { await ExitAsync(); };
            Find<Button>("CheckUpdatesButton").Click += async delegate { await CheckForUpdatesAsync(true); };
            Find<Button>("DownloadUpdateButton").Click += async delegate { await DownloadAvailableUpdateAsync(); };
            Find<Button>("OpenUpdateFolderButton").Click += delegate
            {
                Process.Start("explorer.exe", ProcessTools.Quote(_paths.UpdateRoot));
            };

            Find<Button>("OpenLogsButton").Click += delegate
            {
                Process.Start("explorer.exe", ProcessTools.Quote(_paths.LogRoot));
            };
            Find<Button>("CopyLogsButton").Click += delegate
            {
                string logText = Find<TextBox>("LogBox").Text;
                if (!String.IsNullOrWhiteSpace(logText)) Clipboard.SetText(logText);
            };
            Find<Button>("ClearLogsButton").Click += delegate
            {
                if (Confirm("Clear the current activity log?", "Clear logs"))
                {
                    _logger.Clear();
                    RefreshLogs();
                }
            };

            _window.PreviewKeyDown += async delegate(object sender, KeyEventArgs eventArgs)
            {
                if (ShortcutMatches(_settings.Shortcuts.ImportQr, eventArgs))
                {
                    ShowPage("V2Ray");
                    await ImportQrImageAsync();
                    eventArgs.Handled = true;
                }
                else if (ShortcutMatches(_settings.Shortcuts.ImportConfig, eventArgs))
                {
                    ShowPage("V2Ray");
                    ImportConfigFile();
                    eventArgs.Handled = true;
                }
            };

            _window.Closing += async delegate(object sender, System.ComponentModel.CancelEventArgs eventArgs)
            {
                if (_allowExit) return;
                eventArgs.Cancel = true;
                if (_settings.App.MinimizeToTray)
                {
                    HideToTray();
                    return;
                }
                await ExitAsync();
            };
        }

        private async Task ConnectAsync()
        {
            bool profileReady = ActiveProfile.IsSsh
                ? SaveConnectionControls(true)
                : ActiveProfile.IsPsiphon
                    ? SavePsiphonControls(true)
                    : ValidateProxyProfile(ActiveProfile, true);
            bool coreReady = ActiveProfile.IsSsh || ActiveProfile.IsPsiphon || SaveSingBoxControls(true);
            if (!profileReady || !coreReady || !SaveRoutingModeControls(true)
                || !SaveNekoControls() || !SaveAppControls()) return;
            try
            {
                _manualDisconnectRequested = false;
                CancelPendingFailover();
                if (!_failoverInProgress) _failoverAttemptedProfileIds.Clear();
                Find<Button>("ConnectButton").IsEnabled = false;
                Find<Button>("PowerButton").IsEnabled = false;
                await _manager.ConnectAsync(_settings);
            }
            catch (Exception exception)
            {
                _logger.Error("Connection could not start: " + exception.Message);
                UpdateConnectionVisual(ConnectionState.Error, exception.Message);
                ShowError(exception.Message);
            }
        }

        private void OpenSshQuickStart()
        {
            ConnectionProfile ssh = ActiveProfile.IsSsh
                ? ActiveProfile
                : _settings.Profiles.Items.FirstOrDefault(delegate(ConnectionProfile profile)
                {
                    return profile != null && profile.IsSsh;
                });
            if (ssh == null)
            {
                ShowError("No SSH profile is available. Open Servers and create an SSH profile first.");
                return;
            }
            if (_manager.IsRunning && !ActiveProfile.IsSsh)
            {
                ShowError("Disconnect the current V2Ray connection before switching to SSH.");
                return;
            }
            if (!_manager.IsRunning && !String.Equals(ssh.Id, ActiveProfileId, StringComparison.OrdinalIgnoreCase))
                ActivateProfileInternal(ssh, "Quick start");
            ShowPage("Connection");
        }

        private void OpenV2RayQuickStart(bool focusImport)
        {
            ConnectionProfile proxy = !ActiveProfile.IsSsh && !ActiveProfile.IsPsiphon
                ? ActiveProfile
                : _settings.Profiles.Items.FirstOrDefault(delegate(ConnectionProfile profile)
                {
                    return profile != null && !profile.IsSsh && !profile.IsPsiphon
                        && String.IsNullOrEmpty(GetProxyValidationError(profile));
                });

            if (_manager.IsRunning && (ActiveProfile.IsSsh || ActiveProfile.IsPsiphon))
            {
                ShowError("Disconnect the current connection before switching to V2Ray.");
                return;
            }
            if (!_manager.IsRunning && proxy != null
                && !String.Equals(proxy.Id, ActiveProfileId, StringComparison.OrdinalIgnoreCase))
                ActivateProfileInternal(proxy, "V2Ray section");

            ShowPage("V2Ray");
            RefreshV2RayProfileSelector(proxy == null ? null : proxy.Id);
            if (proxy == null)
            {
                Find<TextBlock>("V2RayProfileStatusText").Text =
                    "No usable V2Ray profile is available. Paste the original config or refresh its subscription.";
                Find<TextBlock>("V2RayProfileStatusText").Foreground = Brush("#FFBD69");
                Find<TextBox>("ConfigImportInput").Focus();
            }
            else if (focusImport)
            {
                Find<TextBox>("ConfigImportInput").Focus();
            }
        }

        private void OpenPsiphonQuickStart()
        {
            ConnectionProfile psiphon = ActiveProfile.IsPsiphon
                ? ActiveProfile
                : _settings.Profiles.Items.FirstOrDefault(delegate(ConnectionProfile profile)
                {
                    return profile != null && profile.IsPsiphon;
                });
            if (_manager.IsRunning && !ActiveProfile.IsPsiphon)
            {
                ShowError("Disconnect the current connection before switching to Psiphon.");
                return;
            }
            if (!_manager.IsRunning && psiphon != null
                && !String.Equals(psiphon.Id, ActiveProfileId, StringComparison.OrdinalIgnoreCase))
                ActivateProfileInternal(psiphon, "Psiphon section");
            ShowPage("Psiphon");
        }

        private void OpenProfileManager(string scope)
        {
            _profileProviderScope = String.Equals(scope, "V2Ray", StringComparison.OrdinalIgnoreCase)
                ? "V2Ray"
                : String.Equals(scope, "SSH", StringComparison.OrdinalIgnoreCase) ? "SSH" : "All";
            RefreshProfileFilters();
            ShowPage("Profiles");
        }

        private async Task EmergencyNetworkResetAsync()
        {
            if (_emergencyResetRunning) return;
            if (!Confirm(
                _localization.Translate(
                    "Disconnect Nivan, clear its Windows LAN proxy values, restore DNS changed by Nivan, and flush the DNS cache?",
                    _settings.App.Language
                ) + "\n\n" + _localization.Translate(
                    "Saved profiles and account details will not be deleted.",
                    _settings.App.Language
                ),
                "Restore normal internet")) return;

            _emergencyResetRunning = true;
            Button button = Find<Button>("EmergencyNetworkResetButton");
            string oldText = button.Content == null ? "Restore normal internet" : button.Content.ToString();
            button.Content = "Restoring...";
            button.IsEnabled = false;
            List<string> completed = new List<string>();
            List<string> errors = new List<string>();
            try
            {
                _manualDisconnectRequested = true;
                CancelPendingFailover();
                CancelQualityTest();
                try
                {
                    await _manager.DisconnectAsync();
                    completed.Add("connection stopped");
                }
                catch (Exception exception) { errors.Add("disconnect: " + exception.Message); }

                try
                {
                    _nekoRay.Stop(_settings.NekoRay);
                    completed.Add("integrated routing stopped");
                }
                catch (Exception exception) { errors.Add("routing: " + exception.Message); }

                try
                {
                    _systemProxy.ClearLanProxyConfiguration();
                    completed.Add("Windows LAN proxy cleared");
                }
                catch (Exception exception) { errors.Add("LAN proxy: " + exception.Message); }

                if (_dns.HasPendingRestore)
                {
                    try
                    {
                        await _dns.RestoreAsync();
                        completed.Add("previous DNS restored");
                    }
                    catch (Exception exception) { errors.Add("DNS restore: " + exception.Message); }
                }
                else
                {
                    try
                    {
                        await ProcessTools.RunHiddenAsync("ipconfig.exe", "/flushdns");
                        completed.Add("DNS cache flushed");
                    }
                    catch (Exception exception) { errors.Add("DNS cache: " + exception.Message); }
                }

                string summary = "Completed: " + String.Join(", ", completed.ToArray()) + ".";
                if (errors.Count == 0)
                {
                    _logger.Warning("Emergency network reset completed without deleting application profiles.");
                    ShowInfo(summary + "\n\nNivan profiles and saved account details were kept.", "Normal internet restored");
                }
                else
                {
                    _logger.Error("Emergency network reset was incomplete: " + String.Join(" | ", errors.ToArray()));
                    ShowError(summary + "\n\nStill needs attention:\n" + String.Join("\n", errors.ToArray()), "Reset incomplete");
                }
                UpdateConnectionVisual(ConnectionState.Offline, "Normal Windows internet settings restored");
            }
            finally
            {
                button.Content = oldText;
                button.IsEnabled = true;
                _emergencyResetRunning = false;
            }
        }

        private async Task DisconnectAsync()
        {
            try
            {
                _manualDisconnectRequested = true;
                CancelPendingFailover();
                CancelQualityTest();
                await _manager.DisconnectAsync();
            }
            catch (Exception exception)
            {
                _logger.Error("Disconnect failed: " + exception.Message);
                ShowError(exception.Message);
            }
        }

        private async Task TogglePowerAsync()
        {
            if (_powerActionRunning) return;
            _powerActionRunning = true;
            Button power = Find<Button>("PowerButton");
            power.IsEnabled = false;
            try
            {
                if (_manager.IsRunning) await DisconnectAsync();
                else await ConnectAsync();
            }
            finally
            {
                _powerActionRunning = false;
                power.IsEnabled = _manager.State != ConnectionState.Stopping;
            }
        }

        private bool SaveConnectionControls(bool showErrors)
        {
            try
            {
                ConnectionProfile active = ActiveProfile;
                if (!active.IsSsh)
                    throw new InvalidOperationException("The active profile is not an SSH profile.");

                TunnelSettings tunnel = active.Tunnel;
                string oldHost = tunnel.Host;
                int oldPort = tunnel.Port;
                string host = Find<TextBox>("HostInput").Text.Trim();
                string username = Find<TextBox>("UserInput").Text.Trim();
                ValidateHostAndUser(host, username);

                tunnel.ProfileId = active.Id;
                tunnel.Host = host;
                tunnel.Port = ReadInteger("PortInput", "SSH port", 1, 65535);
                tunnel.Username = username;
                tunnel.SocksPort = ReadInteger("SocksPortInput", "SOCKS port", 1, 65535);
                tunnel.AuthMode = SelectedTag(Find<ComboBox>("AuthModeInput"));
                tunnel.PrivateKeyPath = Find<TextBox>("KeyPathInput").Text.Trim();
                tunnel.ServerAliveInterval = ReadInteger("KeepAliveInput", "Keep-alive interval", 1, 3600);
                tunnel.ServerAliveCountMax = ReadInteger("KeepAliveCountInput", "Keep-alive limit", 1, 100);
                tunnel.ReconnectDelaySeconds = ReadInteger("ReconnectDelayInput", "Reconnect delay", 1, 3600);
                tunnel.AutoReconnect = Find<CheckBox>("AutoReconnectCheck").IsChecked == true;
                tunnel.ClearOldHostKeyOnConnect = true;

                if (String.Equals(tunnel.AuthMode, "Password", StringComparison.OrdinalIgnoreCase))
                {
                    PasswordBox input = Find<PasswordBox>("PasswordInput");
                    if (!String.IsNullOrEmpty(input.Password))
                    {
                        _credentials.Save(active.Id, input.Password);
                        tunnel.UseSavedPassword = true;
                        Find<CheckBox>("AutoLoginCheck").IsChecked = true;
                        input.Clear();
                    }
                    else
                    {
                        tunnel.UseSavedPassword = Find<CheckBox>("AutoLoginCheck").IsChecked == true;
                    }
                    if (tunnel.UseSavedPassword && !_credentials.Exists(active.Id))
                        throw new InvalidOperationException("Enter and save the SSH password for this profile first.");
                }
                else
                {
                    if (!File.Exists(tunnel.PrivateKeyPath))
                        throw new InvalidOperationException("Select a valid private-key file.");
                    tunnel.UseSavedPassword = false;
                    Find<CheckBox>("AutoLoginCheck").IsChecked = false;
                }

                active.Tunnel = tunnel;
                _settings.Tunnel = tunnel;
                if (!String.Equals(oldHost, active.Tunnel.Host, StringComparison.OrdinalIgnoreCase) || oldPort != active.Tunnel.Port)
                    ResetProfileHealth(active);
                _settingsService.Save(_settings);
                UpdatePasswordStatus();
                UpdateEndpointLabels();
                RefreshProfileFilters();
                RefreshProfileList(active.Id);
                return true;
            }
            catch (Exception exception)
            {
                if (showErrors || !_manager.IsRunning)
                    ShowError(exception.Message, "Invalid connection settings");
                return false;
            }
        }

        private bool SaveSingBoxControls(bool showErrors)
        {
            try
            {
                _settings.SingBox.UseBundledCore = true;
                _settings.SingBox.ExecutablePath = _paths.BundledNekoCorePath;
                _integrity.VerifyBundled(
                    _paths.BundledNekoCorePath,
                    _paths.IntegrityManifestPath,
                    "tools/nekoray/nekobox_core.exe"
                );
                _settings.SingBox.AutoReconnect = Find<CheckBox>("SingBoxAutoReconnectCheck").IsChecked == true;
                _settings.SingBox.ReconnectDelaySeconds = ReadInteger(
                    "SingBoxReconnectDelayInput",
                    "sing-box reconnect delay",
                    1,
                    3600
                );
                _settingsService.Save(_settings);
                return true;
            }
            catch (Exception exception)
            {
                if (showErrors) ShowError(exception.Message, "Invalid sing-box settings");
                return false;
            }
        }

        private bool SaveNekoControls()
        {
            try
            {
                _settings.NekoRay.Enabled = true;
                _settings.NekoRay.UseBundledPortable = true;
                _settings.NekoRay.ExecutablePath = _paths.BundledNekoCorePath;
                _settings.NekoRay.Arguments = String.Empty;
                _settings.NekoRay.AutoStart = true;
                _settings.NekoRay.CloseWithTunnel = true;
                ApplyRoutingModeToSettings();
                _settings.NekoRay.MixedPort = ReadInteger(
                    "NekoMixedPortInput",
                    "Integrated proxy port",
                    1024,
                    65535
                );
                if (_settings.Tunnel != null && _settings.NekoRay.MixedPort == _settings.Tunnel.SocksPort)
                    throw new InvalidOperationException("The integrated proxy port must be different from the SSH SOCKS port.");
                double delay;
                if (!Double.TryParse(Find<TextBox>("NekoDelayInput").Text.Trim(), out delay) || delay < 0 || delay > 3600)
                    throw new InvalidOperationException("Enter a valid routing start delay.");
                _settings.NekoRay.StartDelaySeconds = delay;
                _settingsService.Save(_settings);
                return true;
            }
            catch (Exception exception)
            {
                ShowError(exception.Message, "Invalid routing settings");
                return false;
            }
        }

        private bool SaveRoutingModeControls(bool showErrors)
        {
            try
            {
                ApplyRoutingModeToSettings();
                if (showErrors
                    && String.Equals(_settings.NekoRay.RoutingMode, RoutingModes.SelectedApps, StringComparison.OrdinalIgnoreCase)
                    && String.IsNullOrWhiteSpace(_settings.NekoRay.SelectedAppProcesses))
                    throw new InvalidOperationException("Add at least one application for Selected Applications mode.");
                _settingsService.Save(_settings);
                UpdateRoutingModeUi();
                return true;
            }
            catch (Exception exception)
            {
                if (showErrors) ShowError(exception.Message, "Routing mode");
                return false;
            }
        }

        private void ApplyRoutingModeToSettings()
        {
            string mode = SelectedTag(Find<ComboBox>("RoutingModeInput"));
            if (!RoutingModes.IsValid(mode)) mode = RoutingModes.BrowserOnly;
            _settings.NekoRay.RoutingMode = mode;
            _settings.NekoRay.SelectedAppProcesses = SplitTunnelRuleBuilder.NormalizeProcessList(
                Find<TextBox>("SelectedAppsInput").Text
            );
            if (String.Equals(mode, RoutingModes.BrowserOnly, StringComparison.OrdinalIgnoreCase))
            {
                _settings.NekoRay.EnableSystemProxy = false;
                _settings.NekoRay.EnableTunMode = false;
            }
            else if (String.Equals(mode, RoutingModes.SystemProxy, StringComparison.OrdinalIgnoreCase))
            {
                _settings.NekoRay.EnableSystemProxy = true;
                _settings.NekoRay.EnableTunMode = false;
            }
            else if (String.Equals(mode, RoutingModes.WholeDevice, StringComparison.OrdinalIgnoreCase))
            {
                _settings.NekoRay.EnableSystemProxy = true;
                _settings.NekoRay.EnableTunMode = true;
            }
            else
            {
                _settings.NekoRay.EnableSystemProxy = false;
                _settings.NekoRay.EnableTunMode = true;
            }
            Find<CheckBox>("NekoSystemProxyCheck").IsChecked = _settings.NekoRay.EnableSystemProxy;
            Find<CheckBox>("NekoTunCheck").IsChecked = _settings.NekoRay.EnableTunMode;
        }

        private void UpdateRoutingModeUi()
        {
            string mode = SelectedTag(Find<ComboBox>("RoutingModeInput"));
            Find<FrameworkElement>("SelectedAppsPanel").Visibility = String.Equals(
                mode,
                RoutingModes.SelectedApps,
                StringComparison.OrdinalIgnoreCase
            ) ? Visibility.Visible : Visibility.Collapsed;
            string detail;
            if (String.Equals(mode, RoutingModes.BrowserOnly, StringComparison.OrdinalIgnoreCase))
                detail = "Only the protected Edge/Chrome window uses the connection. Other applications stay direct.";
            else if (String.Equals(mode, RoutingModes.SelectedApps, StringComparison.OrdinalIgnoreCase))
                detail = "TUN is enabled only for the executable names you choose below.";
            else if (String.Equals(mode, RoutingModes.WholeDevice, StringComparison.OrdinalIgnoreCase))
                detail = "Recommended: TUN routes all Windows traffic and System Proxy is enabled for compatible applications.";
            else
                detail = "Windows System Proxy is enabled for browsers and compatible applications; TUN stays off.";
            Find<TextBlock>("RoutingModeDetailText").Text = detail;
            Find<Button>("OpenProtectedBrowserButton").Visibility = String.Equals(
                mode,
                RoutingModes.BrowserOnly,
                StringComparison.OrdinalIgnoreCase
            ) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AddSelectedApplication()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "Choose an application to route through Nivan";
            dialog.Filter = "Windows applications|*.exe";
            dialog.Multiselect = true;
            if (dialog.ShowDialog(_window) != true) return;
            List<string> processes = Find<TextBox>("SelectedAppsInput").Text
                .Split(new char[] { ';', ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(delegate(string value) { return value.Trim(); })
                .Where(delegate(string value) { return value.Length > 0; })
                .ToList();
            foreach (string file in dialog.FileNames.Take(32))
            {
                string name = System.IO.Path.GetFileName(file);
                if (!processes.Contains(name, StringComparer.OrdinalIgnoreCase)) processes.Add(name);
            }
            Find<TextBox>("SelectedAppsInput").Text = String.Join(";", processes.Take(32));
            SaveRoutingModeControls(false);
        }

        private void OpenProtectedBrowser()
        {
            try
            {
                if (_manager.State != ConnectionState.Connected)
                    throw new InvalidOperationException("Connect first, then open the protected browser.");
                BrowserChoice browser = Find<ComboBox>("BrowserInput").SelectedItem as BrowserChoice;
                _browserProxy.Launch(browser, ActiveProfile.LocalSocksPort);
                _logger.Info("Protected browser launched with an isolated proxy profile.");
            }
            catch (Exception exception) { ShowError(exception.Message, "Protected browser"); }
        }

        private async Task RunDiagnosticsAsync()
        {
            Button first = Find<Button>("RunDiagnosticsButton");
            Button second = Find<Button>("OpenDiagnosticsButton");
            first.IsEnabled = false;
            second.IsEnabled = false;
            Find<TextBlock>("DiagnosticStatusText").Text = "Running connection diagnosis...";
            Find<TextBlock>("DiagnosticDetailText").Text = "Checking provider and network path.";
            try
            {
                ConnectionDiagnosticResult result = await _diagnostics.RunAsync(
                    ActiveProfile,
                    _manager.State,
                    _settings.NekoRay.RoutingMode,
                    _settings.NekoRay.SelectedAppProcesses,
                    _settings.NekoRay.MixedPort,
                    CancellationToken.None
                );
                string details = String.Join(Environment.NewLine, result.Steps.Select(delegate(DiagnosticStepResult step)
                {
                    return (step.Success ? "✓ " : step.Warning ? "! " : "✕ ") + step.Name + ": " + step.Detail;
                }));
                Find<TextBlock>("DiagnosticStatusText").Text = result.Success ? "Connection path is healthy" : "Connection needs attention";
                Find<TextBlock>("DiagnosticStatusText").Foreground = result.Success ? Brush("#39DBA0") : Brush("#FF667A");
                Find<TextBlock>("DiagnosticDetailText").Text = result.Summary + Environment.NewLine + details;
                _logger.Info("Connection diagnosis completed: " + result.Summary);
            }
            catch (Exception exception)
            {
                Find<TextBlock>("DiagnosticStatusText").Text = "Diagnosis failed";
                Find<TextBlock>("DiagnosticDetailText").Text = exception.Message;
                Find<TextBlock>("DiagnosticStatusText").Foreground = Brush("#FF667A");
            }
            finally
            {
                first.IsEnabled = true;
                second.IsEnabled = true;
            }
        }

        private bool SaveAppControls()
        {
            try
            {
                _settings.App.MinimizeToTray = Find<CheckBox>("MinimizeToTrayCheck").IsChecked == true;
                _settings.App.StartMinimized = Find<CheckBox>("StartMinimizedCheck").IsChecked == true;
                _settings.App.ConfirmExit = Find<CheckBox>("ConfirmExitCheck").IsChecked == true;
                _settings.App.EnableLanProxyOnProxyConnect = Find<CheckBox>("EnableLanProxyOnConnectCheck").IsChecked == true;
                _settings.App.DisableLanProxyOnDisconnect = Find<CheckBox>("DisableLanProxyOnDisconnectCheck").IsChecked == true;
                _settings.Health.AutoCheckAfterConnect = Find<CheckBox>("AutoHealthCheckAfterConnectCheck").IsChecked == true;
                _settings.Health.LatencySamples = ReadInteger("HealthLatencySamplesInput", "Latency samples", 3, 10);
                _settings.Health.QuickDownloadMegabytes = ReadInteger("QuickDownloadSizeInput", "Quick download size", 1, 20);
                _settings.Health.FullDownloadMegabytes = ReadInteger("FullDownloadSizeInput", "Full download size", 5, 100);
                _settings.Health.FullUploadMegabytes = ReadInteger("FullUploadSizeInput", "Full upload size", 1, 50);
                _settings.Automation.EnableAutoFailover = Find<CheckBox>("AutoFailoverCheck").IsChecked == true;
                _settings.Automation.PreferFavorites = Find<CheckBox>("PreferFavoritesCheck").IsChecked == true;
                _settings.Automation.FailoverDelaySeconds = ReadInteger("FailoverDelayInput", "Failover delay", 5, 300);
                _settings.Automation.MaximumFailoverAttempts = ReadInteger("FailoverMaxAttemptsInput", "Maximum failover attempts", 1, 20);
                _settings.Network.RecoverLanProxyAfterCrash = Find<CheckBox>("RecoverProxyAfterCrashCheck").IsChecked == true;
                _settings.Network.EnableProxyNetworkLock = Find<CheckBox>("ProxyNetworkLockCheck").IsChecked == true;
                _settings.Network.EnableSplitTunneling = Find<CheckBox>("SplitTunnelingCheck").IsChecked == true;
                _settings.Network.ProxyBypassList = Find<TextBox>("ProxyBypassListInput").Text.Trim();
                _settings.Network.TunBypassProcesses = Find<TextBox>("TunBypassProcessesInput").Text.Trim();
                _settings.Network.TunBypassDomains = Find<TextBox>("TunBypassDomainsInput").Text.Trim();
                _settings.Network.TunBypassIpCidrs = Find<TextBox>("TunBypassCidrsInput").Text.Trim();
                _settings.Shortcuts = ReadShortcutControls();
                _settings.Updates.CheckOnStartup = Find<CheckBox>("CheckUpdatesOnStartupCheck").IsChecked == true;
                _settings.Updates.ManifestUrl = Find<TextBox>("UpdateManifestUrlInput").Text.Trim();
                _settingsService.Save(_settings);
                UpdateShortcutHint(_settings.Shortcuts);
                return true;
            }
            catch (Exception exception)
            {
                ShowError(exception.Message, "Invalid preferences");
                return false;
            }
        }

        private void LoadControlsFromSettings()
        {
            if (ActiveProfile.IsSsh) LoadConnectionControlsFromActiveProfile();

            SelectComboTag(Find<ComboBox>("LanguageInput"), _settings.App.Language);

            Find<TextBox>("SingBoxPathInput").Text = _settings.SingBox.ExecutablePath;
            Find<CheckBox>("UseBundledCoreCheck").IsChecked = _settings.SingBox.UseBundledCore;
            Find<CheckBox>("SingBoxAutoReconnectCheck").IsChecked = _settings.SingBox.AutoReconnect;
            Find<TextBox>("SingBoxReconnectDelayInput").Text = _settings.SingBox.ReconnectDelaySeconds.ToString();
            Find<TextBlock>("SingBoxStatusText").Text = File.Exists(_paths.BundledNekoCorePath)
                ? "Reviewed portable core is included. Use Check core to verify its compiled-in fingerprint."
                : "Portable core is missing. Extract the complete package again.";

            Find<CheckBox>("NekoEnabledCheck").IsChecked = _settings.NekoRay.Enabled;
            Find<CheckBox>("NekoAutoStartCheck").IsChecked = _settings.NekoRay.AutoStart;
            Find<CheckBox>("NekoSystemProxyCheck").IsChecked = _settings.NekoRay.EnableSystemProxy;
            Find<CheckBox>("NekoTunCheck").IsChecked = _settings.NekoRay.EnableTunMode;
            Find<CheckBox>("NekoCloseCheck").IsChecked = true;
            Find<TextBox>("NekoDelayInput").Text = _settings.NekoRay.StartDelaySeconds.ToString("0.##");
            Find<TextBox>("NekoMixedPortInput").Text = _settings.NekoRay.MixedPort.ToString();
            ApplyPortableEngineChoiceUi();
            SelectComboTag(Find<ComboBox>("RoutingModeInput"), _settings.NekoRay.RoutingMode);
            Find<TextBox>("SelectedAppsInput").Text = _settings.NekoRay.SelectedAppProcesses;
            _browserChoices = _browserProxy.Discover();
            Find<ComboBox>("BrowserInput").ItemsSource = _browserChoices;
            if (_browserChoices.Count > 0) Find<ComboBox>("BrowserInput").SelectedIndex = 0;
            UpdateRoutingModeUi();

            Find<TextBox>("PsiphonCorePathInput").Text = _settings.Psiphon.ExecutablePath;
            Find<TextBox>("PsiphonConfigPathInput").Text = _settings.Psiphon.ConfigPath;
            Find<TextBox>("PsiphonSocksPortInput").Text = _settings.Psiphon.LocalSocksPort.ToString();
            Find<TextBox>("PsiphonHttpPortInput").Text = _settings.Psiphon.LocalHttpPort.ToString();
            Find<TextBox>("PsiphonRegionInput").Text = _settings.Psiphon.Region;
            Find<CheckBox>("PsiphonAutoReconnectCheck").IsChecked = _settings.Psiphon.AutoReconnect;
            Find<TextBox>("PsiphonReconnectDelayInput").Text = _settings.Psiphon.ReconnectDelaySeconds.ToString();
            UpdatePsiphonStatus();

            Find<TextBox>("CustomDnsNameInput").Text = _settings.Dns.CustomName;
            Find<TextBox>("CustomDnsPrimaryInput").Text = _settings.Dns.CustomPrimary;
            Find<TextBox>("CustomDnsSecondaryInput").Text = _settings.Dns.CustomSecondary;
            Find<CheckBox>("DnsRestoreOnDisconnectCheck").IsChecked = _settings.Dns.RestoreOnDisconnect;
            Find<CheckBox>("DnsRestoreAfterCrashCheck").IsChecked = _settings.Dns.RestoreAfterCrash;
            RefreshDnsProviders(_settings.Dns.ActiveProviderId);

            Find<CheckBox>("MinimizeToTrayCheck").IsChecked = _settings.App.MinimizeToTray;
            Find<CheckBox>("StartMinimizedCheck").IsChecked = _settings.App.StartMinimized;
            Find<CheckBox>("ConfirmExitCheck").IsChecked = _settings.App.ConfirmExit;
            Find<CheckBox>("EnableLanProxyOnConnectCheck").IsChecked = _settings.App.EnableLanProxyOnProxyConnect;
            Find<CheckBox>("DisableLanProxyOnDisconnectCheck").IsChecked = _settings.App.DisableLanProxyOnDisconnect;
            Find<CheckBox>("AutoHealthCheckAfterConnectCheck").IsChecked = _settings.Health.AutoCheckAfterConnect;
            Find<TextBox>("HealthLatencySamplesInput").Text = _settings.Health.LatencySamples.ToString();
            Find<TextBox>("QuickDownloadSizeInput").Text = _settings.Health.QuickDownloadMegabytes.ToString();
            Find<TextBox>("FullDownloadSizeInput").Text = _settings.Health.FullDownloadMegabytes.ToString();
            Find<TextBox>("FullUploadSizeInput").Text = _settings.Health.FullUploadMegabytes.ToString();
            Find<CheckBox>("AutoFailoverCheck").IsChecked = _settings.Automation.EnableAutoFailover;
            Find<CheckBox>("PreferFavoritesCheck").IsChecked = _settings.Automation.PreferFavorites;
            Find<TextBox>("FailoverDelayInput").Text = _settings.Automation.FailoverDelaySeconds.ToString();
            Find<TextBox>("FailoverMaxAttemptsInput").Text = _settings.Automation.MaximumFailoverAttempts.ToString();
            Find<CheckBox>("RecoverProxyAfterCrashCheck").IsChecked = _settings.Network.RecoverLanProxyAfterCrash;
            Find<CheckBox>("ProxyNetworkLockCheck").IsChecked = _settings.Network.EnableProxyNetworkLock;
            Find<CheckBox>("SplitTunnelingCheck").IsChecked = _settings.Network.EnableSplitTunneling;
            Find<TextBox>("ProxyBypassListInput").Text = _settings.Network.ProxyBypassList;
            Find<TextBox>("TunBypassProcessesInput").Text = _settings.Network.TunBypassProcesses;
            Find<TextBox>("TunBypassDomainsInput").Text = _settings.Network.TunBypassDomains;
            Find<TextBox>("TunBypassCidrsInput").Text = _settings.Network.TunBypassIpCidrs;
            LoadShortcutControls(_settings.Shortcuts);
            Find<CheckBox>("CheckUpdatesOnStartupCheck").IsChecked = _settings.Updates.CheckOnStartup;
            Find<TextBox>("UpdateManifestUrlInput").Text = _settings.Updates.ManifestUrl;
            Find<TextBlock>("UpdateStatusText").Text = _settings.Updates.LastStatus;

            Find<ListBox>("ProfileList").ItemsSource = _profileRows;
            Find<ListBox>("SubscriptionList").ItemsSource = _subscriptionRows;
            RefreshProfileFilters();
            RefreshProfileList(ActiveProfileId);
            RefreshSubscriptionList(null);
            UpdatePasswordStatus();
            UpdateEndpointLabels();
        }

        private ShortcutSettings ReadShortcutControls()
        {
            ShortcutSettings shortcuts = new ShortcutSettings
            {
                ImportConfig = NormalizeShortcut("ShortcutImportConfigInput", "Import config file"),
                ImportQr = NormalizeShortcut("ShortcutImportQrInput", "Import QR image"),
                SelectAllProfiles = NormalizeShortcut("ShortcutSelectAllInput", "Select all profiles"),
                DeleteProfiles = NormalizeShortcut("ShortcutDeleteInput", "Delete profiles"),
                DuplicateProfile = NormalizeShortcut("ShortcutDuplicateInput", "Duplicate profile"),
                NewConnection = NormalizeShortcut("ShortcutNewConnectionInput", "New connection")
            };
            string[] values = new string[]
            {
                shortcuts.ImportConfig, shortcuts.ImportQr, shortcuts.SelectAllProfiles,
                shortcuts.DeleteProfiles, shortcuts.DuplicateProfile, shortcuts.NewConnection
            };
            if (values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Length)
                throw new InvalidOperationException("Keyboard shortcuts must be unique.");
            return shortcuts;
        }

        private string NormalizeShortcut(string controlName, string label)
        {
            string value = Find<TextBox>(controlName).Text.Trim();
            KeyGesture gesture = ParseShortcut(value, label);
            return gesture.GetDisplayStringForCulture(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static KeyGesture ParseShortcut(string value, string label)
        {
            if (String.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException(label + " shortcut cannot be empty.");
            try
            {
                KeyGesture gesture = new KeyGestureConverter().ConvertFromInvariantString(value) as KeyGesture;
                if (gesture == null || gesture.Key == Key.None)
                    throw new FormatException();
                return gesture;
            }
            catch
            {
                throw new InvalidOperationException("Enter a valid shortcut for " + label + ", for example Ctrl+I.");
            }
        }

        private static bool ShortcutMatches(string value, KeyEventArgs eventArgs)
        {
            if (eventArgs == null || String.IsNullOrWhiteSpace(value)) return false;
            try
            {
                KeyGesture gesture = new KeyGestureConverter().ConvertFromInvariantString(value) as KeyGesture;
                Key key = eventArgs.Key == Key.System ? eventArgs.SystemKey : eventArgs.Key;
                return gesture != null && gesture.Key == key && gesture.Modifiers == Keyboard.Modifiers;
            }
            catch { return false; }
        }

        private void LoadShortcutControls(ShortcutSettings shortcuts)
        {
            shortcuts = shortcuts ?? ShortcutSettings.CreateDefault();
            Find<TextBox>("ShortcutImportConfigInput").Text = shortcuts.ImportConfig;
            Find<TextBox>("ShortcutImportQrInput").Text = shortcuts.ImportQr;
            Find<TextBox>("ShortcutSelectAllInput").Text = shortcuts.SelectAllProfiles;
            Find<TextBox>("ShortcutDeleteInput").Text = shortcuts.DeleteProfiles;
            Find<TextBox>("ShortcutDuplicateInput").Text = shortcuts.DuplicateProfile;
            Find<TextBox>("ShortcutNewConnectionInput").Text = shortcuts.NewConnection;
            UpdateShortcutHint(shortcuts);
        }

        private void UpdateShortcutHint(ShortcutSettings shortcuts)
        {
            bool persian = String.Equals(_settings.App.Language, "fa", StringComparison.OrdinalIgnoreCase);
            Find<TextBlock>("ProfileShortcutHintText").Text = persian
                ? "میانبرها: " + shortcuts.SelectAllProfiles + " انتخاب همه  •  "
                    + shortcuts.DeleteProfiles + " حذف  •  "
                    + shortcuts.DuplicateProfile + " کپی  •  "
                    + shortcuts.NewConnection + " اتصال جدید"
                : "Shortcuts: " + shortcuts.SelectAllProfiles + " select all  •  "
                    + shortcuts.DeleteProfiles + " remove  •  "
                    + shortcuts.DuplicateProfile + " duplicate  •  "
                    + shortcuts.NewConnection + " new";
        }

        private void LoadConnectionControlsFromActiveProfile()
        {
            if (!ActiveProfile.IsSsh) return;
            _settings.Tunnel = ActiveProfile.Tunnel;
            _settings.Tunnel.ProfileId = ActiveProfile.Id;
            Find<TextBox>("HostInput").Text = _settings.Tunnel.Host;
            Find<TextBox>("PortInput").Text = _settings.Tunnel.Port.ToString();
            Find<TextBox>("UserInput").Text = _settings.Tunnel.Username;
            Find<TextBox>("SocksPortInput").Text = _settings.Tunnel.SocksPort.ToString();
            Find<TextBox>("KeyPathInput").Text = _settings.Tunnel.PrivateKeyPath;
            Find<PasswordBox>("PasswordInput").Clear();
            Find<TextBox>("KeepAliveInput").Text = _settings.Tunnel.ServerAliveInterval.ToString();
            Find<TextBox>("KeepAliveCountInput").Text = _settings.Tunnel.ServerAliveCountMax.ToString();
            Find<TextBox>("ReconnectDelayInput").Text = _settings.Tunnel.ReconnectDelaySeconds.ToString();
            Find<CheckBox>("AutoReconnectCheck").IsChecked = _settings.Tunnel.AutoReconnect;
            Find<CheckBox>("ClearHostKeyCheck").IsChecked = true;
            Find<CheckBox>("AutoLoginCheck").IsChecked = _settings.Tunnel.UseSavedPassword;
            SelectComboTag(Find<ComboBox>("AuthModeInput"), _settings.Tunnel.AuthMode);
            UpdatePasswordStatus();
            UpdateEndpointLabels();
        }

        private void RefreshProfileFilters()
        {
            ComboBox categoryBox = Find<ComboBox>("ProfileCategoryFilter");
            string previous = SelectedTag(categoryBox);
            if (String.IsNullOrWhiteSpace(previous)) previous = "__all__";

            IEnumerable<ConnectionProfile> scopedProfiles = ProfilesForCurrentScope();
            List<string> categories = scopedProfiles
                .Select(delegate(ConnectionProfile profile) { return profile.Category; })
                .Where(delegate(string category) { return !String.IsNullOrWhiteSpace(category); })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(delegate(string category) { return category; }, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _refreshingProfiles = true;
            categoryBox.Items.Clear();
            categoryBox.Items.Add(new ComboBoxItem { Content = "All categories", Tag = "__all__" });
            foreach (string category in categories)
                categoryBox.Items.Add(new ComboBoxItem { Content = category, Tag = category });
            SelectComboTag(categoryBox, previous);
            _refreshingProfiles = false;
        }

        private void RefreshProfileList(string selectedId)
        {
            if (_profileRows == null) return;
            ConnectionProfile selectedBefore = SelectedProfile;
            string desiredId = !String.IsNullOrWhiteSpace(selectedId)
                ? selectedId
                : selectedBefore == null ? ActiveProfileId : selectedBefore.Id;
            string search = Find<TextBox>("ProfileSearchInput").Text.Trim();
            string category = SelectedTag(Find<ComboBox>("ProfileCategoryFilter"));
            string sort = SelectedTag(Find<ComboBox>("ProfileSortInput"));

            IEnumerable<ConnectionProfile> query = ProfilesForCurrentScope();
            if (!String.IsNullOrWhiteSpace(search))
            {
                query = query.Where(delegate(ConnectionProfile profile)
                {
                    return ContainsIgnoreCase(profile.Name, search)
                        || ContainsIgnoreCase(profile.Category, search)
                        || ContainsIgnoreCase(profile.ServerHost, search)
                        || ContainsIgnoreCase(profile.EndpointDisplay, search)
                        || ContainsIgnoreCase(profile.ProtocolLabel, search);
                });
            }
            if (!String.IsNullOrWhiteSpace(category) && !String.Equals(category, "__all__", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(delegate(ConnectionProfile profile)
                {
                    return String.Equals(profile.Category, category, StringComparison.OrdinalIgnoreCase);
                });
            }

            if (String.Equals(sort, "Latency", StringComparison.OrdinalIgnoreCase))
            {
                query = query.OrderBy(ProfileAvailabilityRank)
                    .ThenBy(ProfileLatencyRank)
                    .ThenBy(delegate(ConnectionProfile profile) { return profile.Name; }, StringComparer.OrdinalIgnoreCase);
            }
            else if (String.Equals(sort, "Name", StringComparison.OrdinalIgnoreCase))
            {
                query = query.OrderBy(delegate(ConnectionProfile profile) { return profile.Name; }, StringComparer.OrdinalIgnoreCase);
            }
            else if (String.Equals(sort, "Category", StringComparison.OrdinalIgnoreCase))
            {
                query = query.OrderBy(delegate(ConnectionProfile profile) { return profile.Category; }, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(delegate(ConnectionProfile profile) { return profile.Name; }, StringComparer.OrdinalIgnoreCase);
            }
            else if (String.Equals(sort, "Protocol", StringComparison.OrdinalIgnoreCase))
            {
                query = query.OrderBy(delegate(ConnectionProfile profile) { return profile.ProtocolLabel; }, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(delegate(ConnectionProfile profile) { return profile.Name; }, StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                string activeId = ActiveProfileId;
                query = query
                    .OrderBy(delegate(ConnectionProfile profile)
                    {
                        return String.Equals(profile.Id, activeId, StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                    })
                    .ThenBy(delegate(ConnectionProfile profile) { return profile.IsFavorite ? 0 : 1; })
                    .ThenBy(ProfileAvailabilityRank)
                    .ThenBy(ProfileLatencyRank)
                    .ThenBy(delegate(ConnectionProfile profile) { return profile.Name; }, StringComparer.OrdinalIgnoreCase);
            }

            _refreshingProfiles = true;
            _profileRows.Clear();
            foreach (ConnectionProfile profile in query)
                _profileRows.Add(new ProfileRowViewModel(profile, String.Equals(profile.Id, ActiveProfileId, StringComparison.OrdinalIgnoreCase)));

            ListBox list = Find<ListBox>("ProfileList");
            list.SelectedItem = null;
            foreach (ProfileRowViewModel row in _profileRows)
            {
                if (String.Equals(row.Id, desiredId, StringComparison.OrdinalIgnoreCase))
                {
                    list.SelectedItem = row;
                    list.ScrollIntoView(row);
                    break;
                }
            }
            if (list.SelectedItem == null && _profileRows.Count > 0) list.SelectedIndex = 0;
            _refreshingProfiles = false;
            LoadSelectedProfileEditor();
            RefreshHomeProfileSelector();
            RefreshV2RayProfileSelector(null);
        }

        private IEnumerable<ConnectionProfile> ProfilesForCurrentScope()
        {
            if (String.Equals(_profileProviderScope, "V2Ray", StringComparison.OrdinalIgnoreCase))
            {
                return _settings.Profiles.Items.Where(delegate(ConnectionProfile profile)
                {
                    return profile != null && !profile.IsSsh && !profile.IsPsiphon;
                });
            }
            if (String.Equals(_profileProviderScope, "SSH", StringComparison.OrdinalIgnoreCase))
            {
                return _settings.Profiles.Items.Where(delegate(ConnectionProfile profile)
                {
                    return profile != null && profile.IsSsh;
                });
            }
            return _settings.Profiles.Items;
        }

        private void RefreshHomeProfileSelector()
        {
            ComboBox input = Find<ComboBox>("HomeProfileInput");
            ConnectionProfile active = ActiveProfile;
            List<ConnectionProfile> profiles = _settings.Profiles.Items
                .OrderBy(delegate(ConnectionProfile profile)
                {
                    return String.Equals(profile.Id, active.Id, StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                })
                .ThenBy(delegate(ConnectionProfile profile) { return profile.IsFavorite ? 0 : 1; })
                .ThenBy(delegate(ConnectionProfile profile) { return profile.Name; }, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _refreshingHomeProfile = true;
            input.ItemsSource = profiles;
            input.SelectedItem = profiles.FirstOrDefault(delegate(ConnectionProfile profile)
            {
                return String.Equals(profile.Id, active.Id, StringComparison.OrdinalIgnoreCase);
            });
            input.IsEnabled = !_manager.IsRunning;
            _refreshingHomeProfile = false;
            UpdateHomeProfileDetail();
        }

        private void UpdateHomeProfileDetail()
        {
            ConnectionProfile profile = ActiveProfile;
            Find<TextBlock>("HomeProfileDetailText").Text = profile.ProtocolLabel + "  •  "
                + profile.EndpointDisplay + "  •  local SOCKS5 127.0.0.1:" + profile.LocalSocksPort;
        }

        private void ActivateHomeProfileSelection()
        {
            if (_refreshingHomeProfile) return;
            ConnectionProfile profile = Find<ComboBox>("HomeProfileInput").SelectedItem as ConnectionProfile;
            if (profile == null) return;
            if (String.Equals(profile.Id, ActiveProfileId, StringComparison.OrdinalIgnoreCase))
            {
                UpdateHomeProfileDetail();
                return;
            }
            if (_manager.IsRunning)
            {
                RefreshHomeProfileSelector();
                ShowError("Disconnect before switching the active connection.", "Active connection");
                return;
            }
            if (!profile.IsSsh && !profile.IsPsiphon && !ValidateProxyProfile(profile, true))
            {
                RefreshHomeProfileSelector();
                return;
            }
            try
            {
                ActivateProfileInternal(profile, "Home");
            }
            catch (Exception exception)
            {
                RefreshHomeProfileSelector();
                ShowError(exception.Message, "Active connection");
            }
        }

        private void LoadSelectedProfileEditor()
        {
            List<ConnectionProfile> selectedProfiles = SelectedProfiles;
            ConnectionProfile profile = SelectedProfile;
            bool enabled = profile != null && selectedProfiles.Count == 1;
            string[] editorNames = new string[]
            {
                "ProfileNameInput", "ProfileCategoryInput", "ProfileFavoriteCheck", "ProfileHostInput",
                "ProfilePortInput", "ProfileUserInput", "ProfileSocksPortInput", "ProxyLocalPortInput", "SaveProfileButton",
                "SetActiveProfileButton", "TestSelectedProfileButton", "EditAdvancedProfileButton"
            };
            foreach (string name in editorNames) Find<Control>(name).IsEnabled = enabled && !_testingProfiles;
            Find<Button>("SelectAllProfilesButton").IsEnabled = _profileRows.Count > 0 && !_testingProfiles;
            Find<Button>("DuplicateProfileButton").IsEnabled = enabled && !_testingProfiles;
            Find<Button>("DeleteProfileButton").IsEnabled = selectedProfiles.Count > 0
                && !_testingProfiles;

            if (profile == null)
            {
                Find<TextBox>("ProfileNameInput").Text = String.Empty;
                Find<TextBox>("ProfileCategoryInput").Text = String.Empty;
                Find<CheckBox>("ProfileFavoriteCheck").IsChecked = false;
                Find<TextBox>("ProfileHostInput").Text = String.Empty;
                Find<TextBox>("ProfilePortInput").Text = String.Empty;
                Find<TextBox>("ProfileUserInput").Text = String.Empty;
                Find<TextBox>("ProfileSocksPortInput").Text = String.Empty;
                Find<TextBox>("ProxyLocalPortInput").Text = String.Empty;
                Find<TextBlock>("ProfileEngineText").Text = "Engine: —";
                Find<FrameworkElement>("SshProfileEditorPanel").Visibility = Visibility.Collapsed;
                Find<FrameworkElement>("ProxyProfileEditorPanel").Visibility = Visibility.Collapsed;
                Find<TextBlock>("SelectedProfileStatusText").Text = selectedProfiles.Count > 1
                    ? "Selected profiles: " + selectedProfiles.Count
                    : _profileRows.Count == 0 ? "No profile matches this filter" : "Select a profile to edit";
                Find<TextBlock>("ProfileTestDetailText").Text = selectedProfiles.Count > 1
                    ? "Use Delete to remove the selected profiles together."
                    : "No profile selected.";
                return;
            }

            Find<TextBox>("ProfileNameInput").Text = profile.Name;
            Find<TextBox>("ProfileCategoryInput").Text = profile.Category;
            Find<CheckBox>("ProfileFavoriteCheck").IsChecked = profile.IsFavorite;
            Find<TextBlock>("ProfileEngineText").Text = "Engine: " + (profile.IsSsh
                ? "Windows OpenSSH"
                : profile.IsPsiphon ? "Verified Psiphon provider"
                : profile.IsExternalProxy ? "External proxy via sing-box"
                : profile.IsXray ? "Xray-core (XHTTP)" : "sing-box");
            Find<FrameworkElement>("SshProfileEditorPanel").Visibility = profile.IsSsh ? Visibility.Visible : Visibility.Collapsed;
            Find<FrameworkElement>("ProxyProfileEditorPanel").Visibility = profile.IsSsh || profile.IsPsiphon ? Visibility.Collapsed : Visibility.Visible;
            if (profile.IsSsh)
            {
                Find<TextBox>("ProfileHostInput").Text = profile.Tunnel.Host;
                Find<TextBox>("ProfilePortInput").Text = profile.Tunnel.Port.ToString();
                Find<TextBox>("ProfileUserInput").Text = profile.Tunnel.Username;
                Find<TextBox>("ProfileSocksPortInput").Text = profile.Tunnel.SocksPort.ToString();
                Find<Button>("EditAdvancedProfileButton").Content = "Advanced SSH settings";
            }
            else if (profile.IsPsiphon)
            {
                Find<TextBox>("ProxyLocalPortInput").Text = profile.LocalSocksPort.ToString();
                Find<Button>("EditAdvancedProfileButton").Content = "Psiphon settings";
            }
            else
            {
                Find<TextBlock>("ProxyProtocolText").Text = profile.ProtocolLabel;
                Find<TextBlock>("ProxyServerText").Text = profile.ServerHost + ":" + profile.ServerPort;
                string tls = String.IsNullOrWhiteSpace(profile.Proxy.TlsMode) ? "none" : profile.Proxy.TlsMode;
                Find<TextBlock>("ProxyTransportText").Text = profile.Proxy.Transport + "  •  " + tls;
                Find<TextBox>("ProxyLocalPortInput").Text = profile.Proxy.LocalSocksPort.ToString();
                bool secretRequired = !profile.IsExternalProxy;
                bool hasSecret = _proxySecrets.Exists(profile.Id);
                Find<TextBlock>("ProxyCredentialStatusText").Text = hasSecret
                    ? "Credential protected by Windows DPAPI"
                    : secretRequired ? "Encrypted credential is missing — import this config again" : "No proxy password is stored";
                Find<TextBlock>("ProxyCredentialStatusText").Foreground = hasSecret || !secretRequired
                    ? Brush("#39DBA0") : Brush("#FF667A");
                Find<Button>("EditAdvancedProfileButton").Content = "V2Ray manager";
            }
            bool active = String.Equals(profile.Id, ActiveProfileId, StringComparison.OrdinalIgnoreCase);
            Find<TextBlock>("SelectedProfileStatusText").Text = active ? "Active profile" : "Ready to edit";
            UpdateUseProfileButtonState();
            Find<TextBlock>("ProfileTestDetailText").Text = ProfileTestDetail(profile);
        }

        private void UpdateUseProfileButtonState()
        {
            Button button = Find<Button>("SetActiveProfileButton");
            ConnectionProfile profile = SelectedProfile;
            if (profile == null)
            {
                button.Content = "Use & connect";
                button.IsEnabled = false;
                return;
            }

            bool active = String.Equals(profile.Id, ActiveProfileId, StringComparison.OrdinalIgnoreCase);
            if (_manager.State == ConnectionState.Connected && active)
            {
                button.Content = "Connected";
                button.IsEnabled = false;
            }
            else if (_manager.IsRunning)
            {
                button.Content = active ? "Connection running" : "Disconnect to switch";
                button.IsEnabled = false;
            }
            else
            {
                button.Content = active ? "Connect" : "Use & connect";
                button.IsEnabled = !_testingProfiles;
            }
        }

        private bool SaveSelectedProfile(bool showErrors)
        {
            ConnectionProfile profile = SelectedProfile;
            if (profile == null) return false;
            try
            {
                string name = Find<TextBox>("ProfileNameInput").Text.Trim();
                string category = Find<TextBox>("ProfileCategoryInput").Text.Trim();
                if (String.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Enter a profile name.");
                if (String.IsNullOrWhiteSpace(category)) throw new InvalidOperationException("Enter a category.");

                profile.Name = name;
                profile.Category = category;
                profile.IsFavorite = Find<CheckBox>("ProfileFavoriteCheck").IsChecked == true;

                if (profile.IsSsh)
                {
                    string host = Find<TextBox>("ProfileHostInput").Text.Trim();
                    string username = Find<TextBox>("ProfileUserInput").Text.Trim();
                    ValidateHostAndUser(host, username);
                    int port = ReadInteger("ProfilePortInput", "SSH port", 1, 65535);
                    int socksPort = ReadInteger("ProfileSocksPortInput", "SOCKS port", 1, 65535);
                    bool endpointChanged = !String.Equals(profile.Tunnel.Host, host, StringComparison.OrdinalIgnoreCase)
                        || profile.Tunnel.Port != port;
                    profile.Tunnel.ProfileId = profile.Id;
                    profile.Tunnel.Host = host;
                    profile.Tunnel.Port = port;
                    profile.Tunnel.Username = username;
                    profile.Tunnel.SocksPort = socksPort;
                    if (endpointChanged) ResetProfileHealth(profile);
                }
                else if (profile.IsPsiphon)
                {
                    profile.LocalSocksPort = _settings.Psiphon.LocalSocksPort;
                }
                else
                {
                    profile.Proxy.LocalSocksPort = ReadInteger("ProxyLocalPortInput", "SOCKS port", 1, 65535);
                    string proxyError = GetProxyValidationError(profile);
                    if (!String.IsNullOrEmpty(proxyError))
                        throw new InvalidOperationException(proxyError);
                }

                if (String.Equals(profile.Id, ActiveProfileId, StringComparison.OrdinalIgnoreCase))
                {
                    if (profile.IsSsh)
                    {
                        _settings.Tunnel = profile.Tunnel;
                        LoadConnectionControlsFromActiveProfile();
                    }
                    UpdateEndpointLabels();
                }
                _settingsService.Save(_settings);
                RefreshProfileFilters();
                RefreshProfileList(profile.Id);
                return true;
            }
            catch (Exception exception)
            {
                if (showErrors) ShowError(exception.Message, "Invalid profile");
                return false;
            }
        }

        private void CreateProfile()
        {
            string id = "ssh-" + Guid.NewGuid().ToString("N");
            ConnectionProfile source = ActiveProfile.IsSsh
                ? ActiveProfile
                : _settings.Profiles.Items.FirstOrDefault(delegate(ConnectionProfile item) { return item.IsSsh; });
            ConnectionProfile profile = source == null
                ? AppSettings.CreateDefault().Profiles.Items[0].Clone(id)
                : source.Clone(id);
            profile.Name = UniqueProfileName("New SSH profile");
            profile.Category = source == null ? "SSH" : source.Category;
            profile.Tunnel.SocksPort = FindNextSocksPort((source == null ? 1080 : source.LocalSocksPort) + 1);
            profile.Tunnel.UseSavedPassword = false;
            _settings.Profiles.Items.Add(profile);
            _settingsService.Save(_settings);
            RefreshProfileFilters();
            RefreshProfileList(profile.Id);
            ShowPage("Profiles");
            _logger.Info("New SSH profile created: " + profile.Name + ".");
        }

        private void ShowNewConnectionMenu()
        {
            Button anchor = Find<Button>("NewProfileButton");
            ContextMenu menu = new ContextMenu
            {
                FlowDirection = String.Equals(_settings.App.Language, "fa", StringComparison.OrdinalIgnoreCase)
                    ? FlowDirection.RightToLeft
                    : FlowDirection.LeftToRight
            };
            AddNewConnectionMenuItem(menu, "SSH account", delegate { CreateProfile(); });
            AddNewConnectionMenuItem(menu, "V2Ray config or QR", delegate
            {
                OpenV2RayQuickStart(false);
                Find<TextBox>("ConfigImportInput").Focus();
            });
            AddNewConnectionMenuItem(menu, "V2Ray subscription", delegate
            {
                OpenV2RayQuickStart(false);
                Find<TextBox>("SubscriptionUrlInput").Focus();
            });
            AddNewConnectionMenuItem(menu, "External SOCKS / HTTP proxy", delegate
            {
                OpenV2RayQuickStart(false);
                Find<TextBox>("ExternalProxyHostInput").Focus();
            });
            AddNewConnectionMenuItem(menu, "Psiphon provider", delegate { OpenPsiphonQuickStart(); });
            menu.PlacementTarget = anchor;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void AddNewConnectionMenuItem(ContextMenu menu, string label, Action action)
        {
            MenuItem item = new MenuItem
            {
                Header = _localization.Translate(label, _settings.App.Language),
                Padding = new Thickness(12, 7, 12, 7)
            };
            item.Click += delegate { action(); };
            menu.Items.Add(item);
        }

        private void DuplicateProfile()
        {
            ConnectionProfile source = SelectedProfile;
            if (source == null) return;
            string id = (source.IsSsh ? "ssh-" : source.IsPsiphon ? "psiphon-" : "proxy-") + Guid.NewGuid().ToString("N");
            ConnectionProfile profile = source.Clone(id);
            profile.Name = UniqueProfileName(profile.Name);
            profile.LocalSocksPort = FindNextSocksPort(source.LocalSocksPort + 1);
            if (source.IsSsh)
            {
                if (_credentials.Copy(source.Id, profile.Id)
                    && String.Equals(profile.Tunnel.AuthMode, "Password", StringComparison.OrdinalIgnoreCase))
                    profile.Tunnel.UseSavedPassword = true;
            }
            else if (!source.IsPsiphon && _proxySecrets.Exists(source.Id) && !_proxySecrets.Copy(source.Id, profile.Id))
            {
                ShowError("The encrypted credential for this imported profile is missing. Import the config again before duplicating it.");
                return;
            }
            _settings.Profiles.Items.Add(profile);
            _settingsService.Save(_settings);
            RefreshProfileFilters();
            RefreshProfileList(profile.Id);
            _logger.Info(profile.ProtocolLabel + " profile duplicated: " + profile.Name + ".");
        }

        private void DeleteSelectedProfiles()
        {
            List<ConnectionProfile> profiles = SelectedProfiles;
            if (profiles.Count == 0) return;
            bool deletingAll = _settings.Profiles.Items.Count == profiles.Count;
            string activeId = ActiveProfileId;
            bool active = profiles.Any(delegate(ConnectionProfile profile)
            {
                return String.Equals(profile.Id, activeId, StringComparison.OrdinalIgnoreCase);
            });
            if (active && _manager.IsRunning)
            {
                ShowError("Disconnect before deleting the active profile.");
                return;
            }
            string prompt = profiles.Count == 1
                ? "Delete profile '" + profiles[0].Name + "' and its encrypted credential?"
                : deletingAll
                    ? "Delete all " + profiles.Count + " saved profiles and their encrypted credentials? Nivan will return to an empty SSH form."
                    : "Delete " + profiles.Count + " selected profiles and their encrypted credentials?";
            if (!Confirm(prompt, profiles.Count == 1 ? "Delete profile" : "Delete selected profiles")) return;

            foreach (ConnectionProfile profile in profiles)
            {
                _settings.Profiles.Items.Remove(profile);
                if (profile.IsSsh) _credentials.Delete(profile.Id);
                else if (!profile.IsPsiphon) _proxySecrets.Delete(profile.Id);
            }
            if (_settings.Profiles.Items.Count == 0)
            {
                ConnectionProfile empty = AppSettings.CreateDefault().Profiles.Items[0];
                _settings.Profiles.Items.Add(empty);
            }
            if (active)
            {
                ConnectionProfile replacement = _settings.Profiles.Items.FirstOrDefault(delegate(ConnectionProfile profile)
                {
                    return profile.IsSsh;
                }) ?? _settings.Profiles.Items[0];
                _settings.Profiles.ActiveProfileId = replacement.Id;
                if (replacement.IsSsh)
                {
                    _settings.Tunnel = replacement.Tunnel;
                    LoadConnectionControlsFromActiveProfile();
                }
            }
            _settingsService.Save(_settings);
            RefreshProfileFilters();
            RefreshProfileList(ActiveProfileId);
            RefreshSubscriptionList(null);
            UpdateEndpointLabels();
            UpdatePasswordStatus();
            UpdateNekoVisual();
            _logger.Info(profiles.Count + " selected connection profile(s) and their local credentials were deleted.");
        }

        private bool ActivateSelectedProfile(bool showNotice)
        {
            ConnectionProfile profile = SelectedProfile;
            if (profile == null) return false;
            if (String.Equals(profile.Id, ActiveProfileId, StringComparison.OrdinalIgnoreCase))
            {
                if (profile.IsSsh && !SaveSelectedProfile(true)) return false;
                if (!profile.IsSsh && !profile.IsPsiphon && !ValidateProxyProfile(profile, true)) return false;
                return true;
            }
            if (_manager.IsRunning)
            {
                ShowError("Disconnect before switching the active profile.");
                return false;
            }
            if (profile.IsSsh && !SaveSelectedProfile(true)) return false;
            if (!profile.IsSsh && !profile.IsPsiphon && !ValidateProxyProfile(profile, true)) return false;
            _settings.Profiles.ActiveProfileId = profile.Id;
            if (profile.IsSsh)
            {
                _settings.Tunnel = profile.Tunnel;
                _settings.Tunnel.ProfileId = profile.Id;
                if (String.Equals(profile.Tunnel.AuthMode, "Password", StringComparison.OrdinalIgnoreCase))
                    profile.Tunnel.UseSavedPassword = _credentials.Exists(profile.Id);
            }
            _settingsService.Save(_settings);
            if (profile.IsSsh) LoadConnectionControlsFromActiveProfile();
            RefreshProfileList(profile.Id);
            UpdateEndpointLabels();
            UpdatePasswordStatus();
            UpdateNekoVisual();
            _logger.Info("Active " + profile.ProtocolLabel + " profile changed to " + profile.Name + ".");
            if (showNotice) ShowInfo("'" + profile.Name + "' is now the active connection profile.");
            return true;
        }

        private async Task UseSelectedProfileAsync()
        {
            ConnectionProfile profile = SelectedProfile;
            if (profile == null || _profileSwitchRunning) return;
            _profileSwitchRunning = true;
            string profileId = profile.Id;
            ShowPage("Profiles");
            try
            {
                if (_manager.IsRunning)
                {
                    await DisconnectAsync();
                    if (_manager.IsRunning)
                    {
                        ShowError("The current connection did not stop completely. Try Disconnect again before switching profiles.");
                        return;
                    }
                }

                profile = _settings.Profiles.Find(profileId);
                if (profile == null) return;
                RefreshProfileList(profileId);
                if (!ActivateSelectedProfile(false)) return;

                await ConnectAsync();
            }
            finally
            {
                _profileSwitchRunning = false;
                ShowPage("Profiles");
            }
        }

        private async Task TestSelectedProfileAsync()
        {
            ConnectionProfile profile = SelectedProfile;
            if (profile == null || _testingProfiles) return;
            if (!SaveSelectedProfile(true)) return;
            if (profile.IsPsiphon)
            {
                ShowPage("Psiphon");
                await TestPsiphonAsync();
                return;
            }

            try
            {
                SetProfileTestingState(true);
                Find<TextBlock>("ProfileTestDetailText").Text = "Testing " + profile.ServerHost + ":" + profile.ServerPort + "...";
                await _profileHealth.TestAsync(profile, 3000);
                _settingsService.Save(_settings);
                RefreshProfileList(profile.Id);
            }
            catch (Exception exception)
            {
                _logger.Warning("Profile test failed: " + exception.Message);
                ShowError(exception.Message, "Profile test");
            }
            finally
            {
                SetProfileTestingState(false);
            }
        }

        private async Task TestAllProfilesAsync()
        {
            if (_testingProfiles) return;
            ConnectionProfile selected = SelectedProfile;
            if (selected != null && !SaveSelectedProfile(true)) return;
            string selectedId = selected == null ? ActiveProfileId : selected.Id;
            string summary = null;

            try
            {
                SetProfileTestingState(true);
                Find<TextBlock>("SelectedProfileStatusText").Text = "Testing profiles in parallel...";
                IList<ProfileHealthResult> results = await _profileHealth.TestAllAsync(
                    ProfilesForCurrentScope().Where(delegate(ConnectionProfile item) { return !item.IsPsiphon; }),
                    3000,
                    6
                );
                _settingsService.Save(_settings);
                RefreshProfileFilters();
                RefreshProfileList(selectedId);
                int online = results.Count(delegate(ProfileHealthResult result) { return result.Success; });
                int offline = results.Count - online;
                summary = online + " online  •  " + offline + " unavailable";
                _logger.Info("Parallel profile test completed: " + online + " online, " + offline + " unavailable.");
            }
            catch (Exception exception)
            {
                _logger.Warning("Parallel profile test failed: " + exception.Message);
                ShowError(exception.Message, "Test all profiles");
            }
            finally
            {
                SetProfileTestingState(false);
                if (!String.IsNullOrWhiteSpace(summary))
                    Find<TextBlock>("SelectedProfileStatusText").Text = summary;
            }
        }

        private async Task SmartConnectAsync()
        {
            if (_smartConnectRunning || _failoverInProgress) return;
            if (_manager.IsRunning)
            {
                ShowError("Disconnect the current connection before using Smart Connect.", "Smart Connect");
                return;
            }

            List<ConnectionProfile> candidates = GetConnectableProfiles(null);
            if (candidates.Count == 0)
            {
                ShowError(
                    "No usable profile was found. Save the SSH password/private key or import a proxy config first.",
                    "Smart Connect"
                );
                return;
            }

            _smartConnectRunning = true;
            _manualDisconnectRequested = false;
            Button button = Find<Button>("SmartConnectButton");
            string oldText = button.Content == null ? "Smart connect" : button.Content.ToString();
            button.Content = "Finding best...";
            button.IsEnabled = false;
            Find<TextBlock>("StatusDetailText").Text = "Testing " + candidates.Count + " usable profiles in parallel...";
            try
            {
                if (_manager.State == ConnectionState.Error)
                    await _manager.DisconnectAsync();
                SmartConnectResult result = await _smartConnect.SelectBestAsync(
                    candidates,
                    _settings.Automation.PreferFavorites,
                    3500
                );
                _settingsService.Save(_settings);
                RefreshProfileFilters();
                RefreshProfileList(result.SelectedProfile == null ? ActiveProfileId : result.SelectedProfile.Id);
                if (result.SelectedProfile == null)
                {
                    UpdateConnectionVisual(ConnectionState.Error, "Smart Connect found no reachable profile.");
                    ShowError("None of the usable profiles responded to the server test.", "Smart Connect");
                    return;
                }

                ActivateProfileInternal(result.SelectedProfile, "Smart Connect");
                ShowPage("Dashboard");
                _logger.Info(
                    "Smart Connect is starting " + result.SelectedProfile.Name + " after testing "
                    + result.TestedCount + " profiles."
                );
                await ConnectAsync();
            }
            catch (Exception exception)
            {
                _logger.Error("Smart Connect failed: " + exception.Message);
                ShowError(exception.Message, "Smart Connect");
            }
            finally
            {
                _smartConnectRunning = false;
                button.Content = oldText;
                button.IsEnabled = _manager.State == ConnectionState.Offline || _manager.State == ConnectionState.Error;
            }
        }

        private List<ConnectionProfile> GetConnectableProfiles(ICollection<string> excludedProfileIds)
        {
            return _settings.Profiles.Items
                .Where(delegate(ConnectionProfile profile)
                {
                    if (!CanConnectProfile(profile)) return false;
                    return excludedProfileIds == null || !excludedProfileIds.Contains(profile.Id);
                })
                .ToList();
        }

        private bool CanConnectProfile(ConnectionProfile profile)
        {
            if (profile == null || String.IsNullOrWhiteSpace(profile.ServerHost)
                || profile.ServerPort < 1 || profile.ServerPort > 65535) return false;
            if (profile.IsSsh)
            {
                if (profile.Tunnel == null) return false;
                if (String.Equals(profile.Tunnel.AuthMode, "Password", StringComparison.OrdinalIgnoreCase))
                    return _credentials.Exists(profile.Id);
                return !String.IsNullOrWhiteSpace(profile.Tunnel.PrivateKeyPath)
                    && File.Exists(profile.Tunnel.PrivateKeyPath);
            }
            if (profile.IsPsiphon) return false;
            return String.IsNullOrEmpty(GetProxyValidationError(profile));
        }

        private void ActivateProfileInternal(ConnectionProfile profile, string reason)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            if (_manager.IsRunning)
                throw new InvalidOperationException("Disconnect before switching connection profiles.");
            _settings.Profiles.ActiveProfileId = profile.Id;
            if (profile.IsSsh)
            {
                _settings.Tunnel = profile.Tunnel;
                _settings.Tunnel.ProfileId = profile.Id;
                if (String.Equals(profile.Tunnel.AuthMode, "Password", StringComparison.OrdinalIgnoreCase))
                    profile.Tunnel.UseSavedPassword = _credentials.Exists(profile.Id);
                LoadConnectionControlsFromActiveProfile();
            }
            _settingsService.Save(_settings);
            RefreshProfileList(profile.Id);
            UpdateEndpointLabels();
            UpdatePasswordStatus();
            UpdateNekoVisual();
            _logger.Info((reason ?? "Automation") + " activated profile " + profile.Name + ".");
        }

        private void HandleFailoverState(ConnectionState state)
        {
            if (state == ConnectionState.Connected)
            {
                CancelPendingFailover();
                _failoverAttemptedProfileIds.Clear();
                return;
            }
            if (!_settings.Automation.EnableAutoFailover || _manualDisconnectRequested || _failoverInProgress)
                return;
            if (state != ConnectionState.Reconnecting && state != ConnectionState.Error) return;
            if (_failoverDelayCancellation != null) return;

            int delaySeconds = state == ConnectionState.Error
                ? 1
                : _settings.Automation.FailoverDelaySeconds;
            BeginFailoverCountdown(delaySeconds);
        }

        private async void BeginFailoverCountdown(int delaySeconds)
        {
            CancellationTokenSource pending = new CancellationTokenSource();
            _failoverDelayCancellation = pending;
            try
            {
                _logger.Warning("Automatic failover will check alternatives in " + delaySeconds + " seconds.");
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, delaySeconds)), pending.Token);
                if (!pending.Token.IsCancellationRequested
                    && !_manualDisconnectRequested
                    && (_manager.State == ConnectionState.Reconnecting || _manager.State == ConnectionState.Error))
                    await ExecuteFailoverAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                _logger.Error("Automatic failover failed: " + exception.Message);
            }
            finally
            {
                if (Object.ReferenceEquals(_failoverDelayCancellation, pending))
                    _failoverDelayCancellation = null;
                pending.Dispose();
            }
        }

        private async Task ExecuteFailoverAsync()
        {
            if (_failoverInProgress || _manualDisconnectRequested) return;
            _failoverInProgress = true;
            CancelQualityTest();
            string failedProfileId = ActiveProfileId;
            _failoverAttemptedProfileIds.Add(failedProfileId);
            try
            {
                if (_failoverAttemptedProfileIds.Count > _settings.Automation.MaximumFailoverAttempts)
                {
                    _logger.Error("Automatic failover stopped after reaching the configured attempt limit.");
                    return;
                }

                List<ConnectionProfile> candidates = GetConnectableProfiles(_failoverAttemptedProfileIds);
                if (candidates.Count == 0)
                {
                    _logger.Error("Automatic failover found no unused, usable connection profile.");
                    return;
                }

                _logger.Warning("Connection recovery is testing " + candidates.Count + " alternative profiles.");
                if (_manager.IsRunning || _manager.State != ConnectionState.Offline)
                    await _manager.DisconnectForFailoverAsync();

                SmartConnectResult result = await _smartConnect.SelectBestAsync(
                    candidates,
                    _settings.Automation.PreferFavorites,
                    3500
                );
                _settingsService.Save(_settings);
                if (result.SelectedProfile == null)
                {
                    _logger.Error("Automatic failover found no reachable alternative profile.");
                    return;
                }

                _failoverAttemptedProfileIds.Add(result.SelectedProfile.Id);
                ActivateProfileInternal(result.SelectedProfile, "Automatic failover");
                _manualDisconnectRequested = false;
                UpdateConnectionVisual(
                    ConnectionState.Starting,
                    "Failover selected " + result.SelectedProfile.Name + ". Starting connection..."
                );
                await _manager.ConnectAsync(_settings);
                if (_trayIcon != null)
                    _trayIcon.ShowBalloonTip(
                        1800,
                        "Nivan Shield failover",
                        "Switched to " + result.SelectedProfile.Name + ".",
                        Forms.ToolTipIcon.Info
                    );
            }
            finally
            {
                _failoverInProgress = false;
                Find<Button>("SmartConnectButton").IsEnabled = _manager.State == ConnectionState.Offline
                    || _manager.State == ConnectionState.Error;
            }
        }

        private void CancelPendingFailover()
        {
            CancellationTokenSource pending = _failoverDelayCancellation;
            _failoverDelayCancellation = null;
            if (pending != null && !pending.IsCancellationRequested) pending.Cancel();
        }

        private void SetProfileTestingState(bool testing)
        {
            _testingProfiles = testing;
            Find<Button>("TestAllProfilesButton").IsEnabled = !testing;
            Find<Button>("NewProfileButton").IsEnabled = !testing;
            LoadSelectedProfileEditor();
        }

        private void OnConnectionStateChanged(object sender, ConnectionStateChangedEventArgs eventArgs)
        {
            _window.Dispatcher.BeginInvoke(new Action(delegate
            {
                if (eventArgs.State == ConnectionState.Connected && !_connectedAt.HasValue)
                    _connectedAt = DateTime.Now;
                if (eventArgs.State == ConnectionState.Offline || eventArgs.State == ConnectionState.Error)
                    _connectedAt = null;
                UpdateConnectionVisual(eventArgs.State, eventArgs.Detail);
                UpdateNekoVisual();
                UpdateQualityTestButtons();
                if (eventArgs.State == ConnectionState.Connected
                    && _settings.Health.AutoCheckAfterConnect)
                    BeginAutoHealthCheck();
                else if (eventArgs.State == ConnectionState.Offline || eventArgs.State == ConnectionState.Error)
                    CancelQualityTest();
                HandleFailoverState(eventArgs.State);
            }));
        }

        private void UpdateConnectionVisual(ConnectionState state, string detail)
        {
            string label;
            string color;
            switch (state)
            {
                case ConnectionState.Connected: label = "Connected"; color = "#39DBA0"; break;
                case ConnectionState.Starting: label = "Starting"; color = "#5DD9FF"; break;
                case ConnectionState.Reconnecting: label = "Reconnecting"; color = "#FFBD69"; break;
                case ConnectionState.Stopping: label = "Stopping"; color = "#FFBD69"; break;
                case ConnectionState.Error: label = "Connection error"; color = "#FF667A"; break;
                default: label = "Offline"; color = "#64738A"; break;
            }
            SolidColorBrush brush = Brush(color);
            Find<TextBlock>("HeaderStatusText").Text = label;
            Find<TextBlock>("SidebarStatusText").Text = label;
            Find<TextBlock>("HeroStatusText").Text = label;
            Find<TextBlock>("StatusDetailText").Text = detail ?? String.Empty;
            Find<TextBlock>("V2RayConnectionStatusText").Text = label + "  •  " + (detail ?? String.Empty);
            Find<TextBlock>("V2RayConnectionStatusText").Foreground = brush;
            Find<Ellipse>("HeaderStatusDot").Fill = brush;
            Find<Ellipse>("SidebarStatusDot").Fill = brush;
            Find<Ellipse>("HeroStatusDot").Fill = brush;
            Find<Ellipse>("HeroRing").Stroke = brush;
            Button power = Find<Button>("PowerButton");
            bool canDisconnect = _manager.IsRunning && state != ConnectionState.Stopping;
            power.IsEnabled = !_powerActionRunning && state != ConnectionState.Stopping;
            power.ToolTip = canDisconnect ? "Disconnect current connection" : "Connect active profile";
            power.Foreground = Brush(canDisconnect ? "#FF8D9B" : "#5DD9FF");
            power.Background = Brush(canDisconnect ? "#2B1A29" : "#14283A");
            power.BorderBrush = Brush(canDisconnect ? "#7A3043" : "#2E7894");
            Find<Button>("ConnectButton").IsEnabled = state == ConnectionState.Offline || state == ConnectionState.Error;
            Find<Button>("DisconnectButton").IsEnabled = state != ConnectionState.Offline;
            Find<Button>("V2RayConnectButton").IsEnabled = state == ConnectionState.Offline || state == ConnectionState.Error;
            Find<Button>("V2RayDisconnectButton").IsEnabled = state != ConnectionState.Offline;
            Find<Button>("SshConnectButton").IsEnabled = state == ConnectionState.Offline || state == ConnectionState.Error;
            Find<Button>("SshDisconnectButton").IsEnabled = state != ConnectionState.Offline;
            Find<Button>("SmartConnectButton").IsEnabled = (state == ConnectionState.Offline || state == ConnectionState.Error)
                && !_smartConnectRunning && !_failoverInProgress;
            Find<ComboBox>("HomeProfileInput").IsEnabled = state == ConnectionState.Offline || state == ConnectionState.Error;
            Find<ComboBox>("V2RayProfileInput").IsEnabled = state == ConnectionState.Offline || state == ConnectionState.Error;
            UpdateV2RayProfileSelection();
            UpdateUseProfileButtonState();
            UpdateEndpointLabels();
            UpdateQualityTestButtons();
        }

        private void UpdateEndpointLabels()
        {
            ConnectionProfile profile = ActiveProfile;
            string endpoint = profile.EndpointDisplay;
            string proxy = "127.0.0.1:" + profile.LocalSocksPort;
            Find<TextBlock>("SidebarEndpointText").Text = endpoint;
            Find<TextBlock>("DashboardEndpointText").Text = endpoint;
            Find<TextBlock>("DashboardServerKindText").Text = profile.ProtocolLabel + " SERVER";
            Find<TextBlock>("DashboardServerText").Text = profile.ServerHost + ":" + profile.ServerPort;
            Find<TextBlock>("DashboardProxyText").Text = proxy;
            Find<TextBlock>("DashboardActiveProfileText").Text = ActiveProfile.Name + "  •  " + ActiveProfile.Category;
            UpdateHomeProfileDetail();
            Button v2rayConnect = Find<Button>("V2RayConnectButton");
            v2rayConnect.Content = profile.IsSsh || profile.IsPsiphon ? "Import a config first" : "Connect " + profile.ProtocolLabel;
            v2rayConnect.IsEnabled = !profile.IsSsh && !profile.IsPsiphon
                && (_manager.State == ConnectionState.Offline || _manager.State == ConnectionState.Error);
            if (!_qualityTestRunning) RefreshHealthDisplay();
        }

        private void UpdatePasswordStatus()
        {
            TextBlock status = Find<TextBlock>("PasswordStatusText");
            Button clear = Find<Button>("ClearSavedPasswordButton");
            if (!ActiveProfile.IsSsh)
            {
                status.Text = "SSH credentials are managed per SSH profile. The active profile uses " + ActiveProfile.ProtocolLabel + ".";
                status.Foreground = Brush("#74859D");
                clear.IsEnabled = false;
                return;
            }
            if (_credentials.Exists(ActiveProfileId))
            {
                status.Text = "An encrypted password is saved for profile '" + ActiveProfile.Name + "'.";
                status.Foreground = Brush("#39DBA0");
                clear.IsEnabled = true;
            }
            else
            {
                status.Text = "No password is saved for profile '" + ActiveProfile.Name + "'.";
                status.Foreground = Brush("#74859D");
                clear.IsEnabled = false;
            }
        }

        private void UpdateNekoVisual()
        {
            bool running = _nekoRay.IsRunning(_settings.NekoRay);
            bool ready = _nekoRay.IsReady;
            string error = _nekoRay.LastError;
            string color = ready ? "#39DBA0" : running ? "#FFBD69" : !String.IsNullOrWhiteSpace(error) ? "#FF667A" : "#64738A";
            SolidColorBrush brush = Brush(color);
            Find<Ellipse>("NekoStatusDot").Fill = brush;
            Find<TextBlock>("NekoStatusLabel").Text = ready
                ? "Integrated routing is active"
                : running ? "Integrated routing is starting"
                : !String.IsNullOrWhiteSpace(error) ? "Routing needs attention"
                : "Routing is idle";
            Process process = _nekoRay.CurrentProcess;
            string modes = RoutingModeLabel();
            Find<TextBlock>("NekoPidLabel").Text = !String.IsNullOrWhiteSpace(error) && !running
                ? error
                : process == null
                    ? "Connect an SSH profile to start " + modes
                    : modes + "  •  local 127.0.0.1:" + _settings.NekoRay.MixedPort + "  •  PID " + process.Id;
            if (ActiveProfile.IsSsh || ActiveProfile.IsPsiphon)
            {
                Find<TextBlock>("DashboardAuxTitleText").Text = ActiveProfile.IsPsiphon ? "PSIPHON ROUTING" : "SSH ROUTING";
                Find<TextBlock>("DashboardNekoStatusText").Text = ready
                    ? "Active"
                    : running ? "Starting"
                    : !String.IsNullOrWhiteSpace(error) ? "Error"
                    : "Ready";
                Find<TextBlock>("DashboardNekoStatusText").Foreground = ready || running || !String.IsNullOrWhiteSpace(error)
                    ? brush
                    : _window.Foreground;
                Find<TextBlock>("DashboardNekoDetailText").Text = ready
                    ? modes + "  •  127.0.0.1:" + _settings.NekoRay.MixedPort
                    : !String.IsNullOrWhiteSpace(error) ? error
                    : String.Equals(_settings.NekoRay.RoutingMode, RoutingModes.BrowserOnly, StringComparison.OrdinalIgnoreCase)
                        ? "Protected browser uses local SOCKS5 directly"
                        : "Starts automatically after the provider connects";
            }
            else
            {
                bool coreRunning = _manager.State != ConnectionState.Offline && _manager.State != ConnectionState.Error;
                Find<TextBlock>("DashboardAuxTitleText").Text = "SING-BOX CORE";
                Find<TextBlock>("DashboardNekoStatusText").Text = coreRunning ? "Running" : "Ready";
                Find<TextBlock>("DashboardNekoStatusText").Foreground = coreRunning ? Brush("#39DBA0") : _window.Foreground;
                Find<TextBlock>("DashboardNekoDetailText").Text = ActiveProfile.ProtocolLabel + "  •  local " + ActiveProfile.LocalSocksPort;
            }
            Find<Button>("LaunchNekoButton").IsEnabled = (ActiveProfile.IsSsh || ActiveProfile.IsPsiphon)
                && _settings.NekoRay.Enabled
                && !String.Equals(_settings.NekoRay.RoutingMode, RoutingModes.BrowserOnly, StringComparison.OrdinalIgnoreCase)
                && _manager.State == ConnectionState.Connected
                && !running;
            Find<Button>("StopNekoButton").IsEnabled = running;
            UpdateQualityTestButtons();
        }

        private string RoutingModeLabel()
        {
            if (String.Equals(_settings.NekoRay.RoutingMode, RoutingModes.BrowserOnly, StringComparison.OrdinalIgnoreCase)) return "Browser only";
            if (String.Equals(_settings.NekoRay.RoutingMode, RoutingModes.SelectedApps, StringComparison.OrdinalIgnoreCase)) return "Selected apps";
            if (String.Equals(_settings.NekoRay.RoutingMode, RoutingModes.WholeDevice, StringComparison.OrdinalIgnoreCase)) return "Whole Windows (TUN + System Proxy)";
            if (String.Equals(_settings.NekoRay.RoutingMode, RoutingModes.SystemProxy, StringComparison.OrdinalIgnoreCase)) return "System Proxy";
            return "automatic routing";
        }

        private void ShowPage(string name)
        {
            string[] pageNames = new string[] { "Dashboard", "Health", "Profiles", "Connection", "V2Ray", "Psiphon", "Dns", "NekoRay", "Logs", "About" };
            foreach (string pageName in pageNames)
            {
                Find<FrameworkElement>(pageName + "Page").Visibility = pageName == name ? Visibility.Visible : Visibility.Collapsed;
                Find<Button>("Nav" + pageName).Tag = pageName == name ? "active" : null;
            }

            string title = name;
            string subtitle = "SSH and V2Ray connection control";
            if (name == "Dashboard")
            {
                title = "Home";
                subtitle = "Connection, traffic mode, and everyday tools";
            }
            else if (name == "Profiles")
            {
                bool v2rayOnly = String.Equals(_profileProviderScope, "V2Ray", StringComparison.OrdinalIgnoreCase);
                bool sshOnly = String.Equals(_profileProviderScope, "SSH", StringComparison.OrdinalIgnoreCase);
                title = v2rayOnly ? "V2Ray profiles" : sshOnly ? "SSH profiles" : "Connections";
                subtitle = v2rayOnly
                    ? "Only imported V2Ray configs are shown here"
                    : sshOnly ? "Only SSH accounts are shown here" : "Choose, test, add, or edit SSH and V2Ray profiles";
                Find<TextBlock>("ProfilesPageTitleText").Text = title;
                Find<TextBlock>("ProfilesPageSubtitleText").Text = subtitle;
                Find<Button>("NewProfileButton").Visibility = Visibility.Visible;
                RefreshProfileFilters();
                RefreshProfileList(null);
            }
            else if (name == "Health")
            {
                title = "Connection health";
                subtitle = "Real VPN latency, jitter, download, and upload";
                RefreshHealthDisplay();
            }
            else if (name == "Connection") subtitle = "Advanced settings for " + ActiveProfile.Name;
            else if (name == "V2Ray")
            {
                title = "V2Ray";
                subtitle = "Paste a config or add a subscription link";
                RefreshV2RayProfileSelector(null);
            }
            else if (name == "Psiphon")
            {
                title = "Psiphon";
                subtitle = "Verified free fallback provider";
                UpdatePsiphonStatus();
            }
            else if (name == "Dns")
            {
                title = "DNS Center";
                subtitle = "Iranian DNS profiles, testing, and safe restore";
                RefreshDnsProviders(_settings.Dns.ActiveProviderId);
            }
            else if (name == "NekoRay")
            {
                title = "Integrated routing";
                subtitle = "System Proxy and TUN powered silently by the bundled Neko core";
            }
            else if (name == "Logs")
            {
                subtitle = "Connection lifecycle and application events";
                RefreshLogs();
            }
            else if (name == "About")
            {
                title = "Settings";
                subtitle = "Application behavior, automation, and security";
            }
            Find<TextBlock>("HeaderTitle").Text = title;
            Find<TextBlock>("HeaderSubtitle").Text = subtitle;
            ApplyLanguage();
        }

        private async Task TestServerAsync()
        {
            if (ActiveProfile.IsPsiphon)
            {
                ShowPage("Psiphon");
                await TestPsiphonAsync();
                return;
            }
            bool ready = ActiveProfile.IsSsh
                ? SaveConnectionControls(false)
                : ValidateProxyProfile(ActiveProfile, true);
            if (!ready) return;
            Button dashboardButton = Find<Button>("TestServerButton");
            Button settingsButton = Find<Button>("TestServerConnectionButton");
            string oldText = dashboardButton.Content == null ? "Test server" : dashboardButton.Content.ToString();
            dashboardButton.Content = "Testing...";
            dashboardButton.IsEnabled = false;
            settingsButton.IsEnabled = false;
            try
            {
                ProfileHealthResult result = await _profileHealth.TestAsync(ActiveProfile, 3000);
                _settingsService.Save(_settings);
                RefreshProfileList(ActiveProfileId);
                if (result.Success)
                    ShowInfo("Server is reachable.\n\nLatency: " + result.LatencyMilliseconds + " ms\nPort: " + ActiveProfile.ServerPort, "Server test");
                else
                    ShowError("Could not reach the " + ActiveProfile.ProtocolLabel + " server.\n\n" + result.Error, "Server test");
            }
            catch (Exception exception)
            {
                _logger.Warning("Server test failed: " + exception.Message);
                ShowError(exception.Message, "Server test");
            }
            finally
            {
                dashboardButton.Content = oldText;
                dashboardButton.IsEnabled = true;
                settingsButton.IsEnabled = true;
            }
        }

        private async Task RunQualityTestAsync(ConnectionQualityTestKind kind, bool showErrors)
        {
            if (_qualityTestRunning) return;
            if (_manager.State != ConnectionState.Connected)
            {
                if (showErrors) ShowError("Connect a profile before testing the VPN path.", "Connection health");
                return;
            }

            int proxyPort;
            try { proxyPort = GetActiveHttpProxyPort(); }
            catch (Exception exception)
            {
                if (showErrors) ShowError(exception.Message, "Connection health");
                return;
            }

            _qualityTestRunning = true;
            _qualityTestCancellation = new CancellationTokenSource();
            UpdateQualityTestButtons();
            Find<TextBlock>("HealthOverallText").Text = kind == ConnectionQualityTestKind.Health
                ? "Checking VPN path..."
                : kind == ConnectionQualityTestKind.Quick ? "Running quick test..." : "Running full speed test...";
            Find<TextBlock>("HealthDetailText").Text = "Traffic is being forced through 127.0.0.1:" + proxyPort + ".";
            Find<TextBlock>("HealthScoreText").Text = "…";
            Find<ProgressBar>("HealthProgressBar").Value = 0;

            IProgress<ConnectionQualityProgress> progress = new Progress<ConnectionQualityProgress>(
                delegate(ConnectionQualityProgress update)
                {
                    Find<ProgressBar>("HealthProgressBar").Value = update.Percent;
                    Find<TextBlock>("HealthProgressStageText").Text = (update.Stage ?? String.Empty).ToUpperInvariant();
                    Find<TextBlock>("HealthProgressDetailText").Text = update.Detail ?? String.Empty;
                }
            );

            try
            {
                ConnectionProfile testedProfile = ActiveProfile;
                ConnectionQualityResult result = await _connectionQuality.RunAsync(
                    testedProfile,
                    proxyPort,
                    _settings.Health,
                    kind,
                    progress,
                    _qualityTestCancellation.Token
                );

                ConnectionTestRecord record = new ConnectionTestRecord
                {
                    TestedUtc = DateTime.UtcNow,
                    ProfileId = testedProfile.Id,
                    ProfileName = testedProfile.Name,
                    TestKind = kind.ToString(),
                    ServerLatencyMilliseconds = result.ServerLatencyMilliseconds,
                    TunnelLatencyMilliseconds = result.TunnelLatencyMilliseconds,
                    JitterMilliseconds = result.JitterMilliseconds,
                    FailureRatePercent = result.FailureRatePercent,
                    DownloadMegabitsPerSecond = result.DownloadMegabitsPerSecond,
                    UploadMegabitsPerSecond = result.UploadMegabitsPerSecond,
                    QualityScore = result.QualityScore,
                    QualityLabel = result.QualityLabel
                };
                _settings.Health.History.Insert(0, record);
                while (_settings.Health.History.Count > 20)
                    _settings.Health.History.RemoveAt(_settings.Health.History.Count - 1);
                _settingsService.Save(_settings);
                ApplyHealthRecord(record);
                RefreshHealthHistory();
            }
            catch (OperationCanceledException)
            {
                Find<TextBlock>("HealthOverallText").Text = "Test cancelled";
                Find<TextBlock>("HealthDetailText").Text = "No result was saved.";
                Find<TextBlock>("HealthProgressStageText").Text = "CANCELLED";
                Find<TextBlock>("HealthProgressDetailText").Text = "The connection test was stopped.";
                _logger.Info("Connection quality test cancelled.");
            }
            catch (Exception exception)
            {
                Find<TextBlock>("HealthScoreText").Text = "!";
                Find<TextBlock>("HealthScoreText").Foreground = Brush("#FF667A");
                Find<TextBlock>("HealthOverallText").Text = "VPN path test failed";
                Find<TextBlock>("HealthDetailText").Text = exception.Message;
                Find<TextBlock>("HealthProgressStageText").Text = "FAILED";
                Find<TextBlock>("HealthProgressDetailText").Text = exception.Message;
                _logger.Warning("Connection quality test failed: " + exception.Message);
                if (showErrors) ShowError(exception.Message, "Connection health");
            }
            finally
            {
                _qualityTestRunning = false;
                if (_qualityTestCancellation != null)
                {
                    _qualityTestCancellation.Dispose();
                    _qualityTestCancellation = null;
                }
                UpdateQualityTestButtons();
            }
        }

        private int GetActiveHttpProxyPort()
        {
            if (_manager.State != ConnectionState.Connected)
                throw new InvalidOperationException("The VPN is not connected.");
            if (ActiveProfile.IsSsh || ActiveProfile.IsPsiphon)
            {
                if (!_nekoRay.IsReady)
                    throw new InvalidOperationException(
                        "The provider is connected, but the local HTTP routing layer is not ready yet. Wait a few seconds and try again."
                    );
                return _settings.NekoRay.MixedPort;
            }
            return ActiveProfile.LocalSocksPort;
        }

        private void CancelQualityTest()
        {
            if (_qualityTestCancellation != null && !_qualityTestCancellation.IsCancellationRequested)
                _qualityTestCancellation.Cancel();
        }

        private void UpdateQualityTestButtons()
        {
            bool proxyReady = _manager.State == ConnectionState.Connected
                && ((!ActiveProfile.IsSsh && !ActiveProfile.IsPsiphon) || _nekoRay.IsReady);
            Find<Button>("QuickQualityTestButton").IsEnabled = proxyReady && !_qualityTestRunning;
            Find<Button>("FullQualityTestButton").IsEnabled = proxyReady && !_qualityTestRunning;
            Find<Button>("CancelQualityTestButton").IsEnabled = _qualityTestRunning;
        }

        private async void BeginAutoHealthCheck()
        {
            if (_qualityTestRunning || !_settings.Health.AutoCheckAfterConnect) return;
            if (_lastAutoHealthAt.HasValue
                && DateTime.UtcNow - _lastAutoHealthAt.Value < TimeSpan.FromSeconds(30)) return;
            _lastAutoHealthAt = DateTime.UtcNow;

            for (int attempt = 0; attempt < 30; attempt++)
            {
                if (_manager.State != ConnectionState.Connected) return;
                if ((!ActiveProfile.IsSsh && !ActiveProfile.IsPsiphon) || _nekoRay.IsReady) break;
                await Task.Delay(500);
            }
            if (_manager.State == ConnectionState.Connected
                && (!ActiveProfile.IsSsh || _nekoRay.IsReady)
                && !_qualityTestRunning)
                await RunQualityTestAsync(ConnectionQualityTestKind.Health, false);
        }

        private void RefreshHealthDisplay()
        {
            ConnectionTestRecord record = _settings.Health.History
                .Where(delegate(ConnectionTestRecord item)
                {
                    return item != null && String.Equals(item.ProfileId, ActiveProfileId, StringComparison.OrdinalIgnoreCase);
                })
                .OrderByDescending(delegate(ConnectionTestRecord item) { return item.TestedUtc; })
                .FirstOrDefault();
            if (record == null)
            {
                Find<TextBlock>("HealthScoreText").Text = "—";
                Find<TextBlock>("HealthScoreText").Foreground = Brush("#5DD9FF");
                Find<TextBlock>("HealthOverallText").Text = "Ready to test";
                Find<TextBlock>("HealthDetailText").Text = "Connect first, then run a quick or full test.";
                Find<TextBlock>("HealthServerLatencyText").Text = "—";
                Find<TextBlock>("HealthTunnelLatencyText").Text = "—";
                Find<TextBlock>("HealthJitterText").Text = "—";
                Find<TextBlock>("HealthFailureRateText").Text = "—";
                Find<TextBlock>("HealthDownloadText").Text = "—";
                Find<TextBlock>("HealthUploadText").Text = "—";
                Find<TextBlock>("DashboardHealthSummaryText").Text = "Connection health not tested";
                Find<TextBlock>("DashboardHealthDetailText").Text = "Measure real VPN latency, jitter, download, and upload.";
            }
            else ApplyHealthRecord(record);
            RefreshHealthHistory();
            UpdateQualityTestButtons();
        }

        private void ApplyHealthRecord(ConnectionTestRecord record)
        {
            string color = record.QualityScore >= 85 ? "#39DBA0"
                : record.QualityScore >= 70 ? "#5DD9FF"
                : record.QualityScore >= 50 ? "#FFBD69"
                : "#FF667A";
            Find<TextBlock>("HealthScoreText").Text = record.QualityScore.ToString();
            Find<TextBlock>("HealthScoreText").Foreground = Brush(color);
            Find<TextBlock>("HealthOverallText").Text = (record.QualityLabel ?? "Tested") + " connection";
            Find<TextBlock>("HealthOverallText").Foreground = Brush(color);
            Find<TextBlock>("HealthDetailText").Text = record.ProfileName + "  •  "
                + record.TestedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm") + "  •  " + record.TestKind;
            Find<TextBlock>("HealthServerLatencyText").Text = record.ServerLatencyMilliseconds >= 0
                ? record.ServerLatencyMilliseconds + " ms" : "Unavailable";
            Find<TextBlock>("HealthTunnelLatencyText").Text = record.TunnelLatencyMilliseconds.ToString("0") + " ms";
            Find<TextBlock>("HealthJitterText").Text = record.JitterMilliseconds.ToString("0.0") + " ms";
            Find<TextBlock>("HealthFailureRateText").Text = record.FailureRatePercent.ToString("0") + "%";
            Find<TextBlock>("HealthDownloadText").Text = record.DownloadMegabitsPerSecond > 0
                ? record.DownloadMegabitsPerSecond.ToString("0.0") + " Mbps" : "Not measured";
            Find<TextBlock>("HealthUploadText").Text = record.UploadMegabitsPerSecond > 0
                ? record.UploadMegabitsPerSecond.ToString("0.0") + " Mbps" : "Not measured";
            Find<TextBlock>("DashboardHealthSummaryText").Text = record.QualityLabel + "  •  "
                + record.QualityScore + "/100";
            Find<TextBlock>("DashboardHealthSummaryText").Foreground = Brush(color);
            Find<TextBlock>("DashboardHealthDetailText").Text = "VPN "
                + record.TunnelLatencyMilliseconds.ToString("0") + " ms  •  jitter "
                + record.JitterMilliseconds.ToString("0") + " ms"
                + (record.DownloadMegabitsPerSecond > 0
                    ? "  •  " + record.DownloadMegabitsPerSecond.ToString("0.0") + " Mbps down"
                    : String.Empty);
        }

        private void RefreshHealthHistory()
        {
            IEnumerable<string> lines = _settings.Health.History
                .Where(delegate(ConnectionTestRecord item) { return item != null; })
                .OrderByDescending(delegate(ConnectionTestRecord item) { return item.TestedUtc; })
                .Take(20)
                .Select(delegate(ConnectionTestRecord item)
                {
                    string speed = item.DownloadMegabitsPerSecond > 0
                        ? " | ↓ " + item.DownloadMegabitsPerSecond.ToString("0.0")
                            + " ↑ " + item.UploadMegabitsPerSecond.ToString("0.0") + " Mbps"
                        : String.Empty;
                    return item.TestedUtc.ToLocalTime().ToString("MM-dd HH:mm")
                        + " | " + item.ProfileName
                        + " | " + item.QualityScore + "/100 " + item.QualityLabel
                        + " | " + item.TunnelLatencyMilliseconds.ToString("0") + " ms"
                        + speed;
                });
            string value = String.Join(Environment.NewLine, lines.ToArray());
            Find<TextBox>("HealthHistoryBox").Text = String.IsNullOrWhiteSpace(value)
                ? "No connection tests have been saved yet."
                : value;
        }

        private async Task ForgetHostKeyAsync()
        {
            if (!ActiveProfile.IsSsh)
            {
                ShowError("Host-key management is available only for SSH profiles.");
                return;
            }
            if (!SaveConnectionControls(false)) return;
            string target = "[" + _settings.Tunnel.Host + "]:" + _settings.Tunnel.Port;
            if (!Confirm("Forget the saved SSH host key for " + target + "?", "Forget SSH host key")) return;
            try
            {
                await ProcessTools.RunHiddenAsync("ssh-keygen.exe", "-R " + ProcessTools.Quote(target));
                _logger.Info("Stored SSH host key removed for " + target + ".");
                ShowInfo("Stored SSH host key removed.");
            }
            catch (Exception exception) { ShowError(exception.Message); }
        }

        private void ClearSavedPassword()
        {
            if (!ActiveProfile.IsSsh)
            {
                ShowError("The active profile does not use an SSH password.");
                return;
            }
            if (!Confirm("Remove the encrypted saved SSH password for '" + ActiveProfile.Name + "'?", "Clear saved password")) return;
            _credentials.Delete(ActiveProfileId);
            _settings.Tunnel.UseSavedPassword = false;
            ActiveProfile.Tunnel.UseSavedPassword = false;
            Find<CheckBox>("AutoLoginCheck").IsChecked = false;
            Find<PasswordBox>("PasswordInput").Clear();
            _settingsService.Save(_settings);
            UpdatePasswordStatus();
        }

        private void BrowsePrivateKey()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "Select SSH private key";
            dialog.Filter = "OpenSSH private keys|id_*;*.pem;*.key|All files|*.*";
            if (dialog.ShowDialog(_window) == true) Find<TextBox>("KeyPathInput").Text = dialog.FileName;
        }

        private bool ValidateProxyProfile(ConnectionProfile profile, bool showErrors)
        {
            string error = GetProxyValidationError(profile);
            if (String.IsNullOrEmpty(error)) return true;
            if (showErrors) ShowError(error, "Invalid imported profile");
            return false;
        }

        private string GetProxyValidationError(ConnectionProfile profile)
        {
            try
            {
                if (profile == null || profile.IsSsh || profile.IsPsiphon || profile.Proxy == null)
                    throw new InvalidOperationException("Select an imported V2Ray profile first.");

                string protocol = (profile.Proxy.Protocol ?? String.Empty).Trim().ToLowerInvariant();
                if (protocol != "vmess" && protocol != "vless" && protocol != "trojan" && protocol != "shadowsocks"
                    && protocol != "socks" && protocol != "socks5" && protocol != "http" && protocol != "https")
                    throw new InvalidOperationException("Unsupported proxy protocol: " + protocol);
                if (String.IsNullOrWhiteSpace(profile.Proxy.Server))
                    throw new InvalidOperationException("The proxy server address is missing.");
                if (profile.Proxy.ServerPort < 1 || profile.Proxy.ServerPort > 65535)
                    throw new InvalidOperationException("The proxy server port is invalid.");
                if (profile.Proxy.LocalSocksPort < 1 || profile.Proxy.LocalSocksPort > 65535)
                    throw new InvalidOperationException("The local SOCKS port is invalid.");
                bool secretRequired = protocol == "vmess" || protocol == "vless"
                    || protocol == "trojan" || protocol == "shadowsocks";
                if (secretRequired)
                {
                    if (!_proxySecrets.Exists(profile.Id))
                        throw new InvalidOperationException(
                            "The encrypted credential is missing. Paste the original config again or refresh its subscription; Nivan will repair this saved profile."
                        );
                    string secret = _proxySecrets.Read(profile.Id);
                    if (String.IsNullOrEmpty(secret))
                        throw new InvalidOperationException("The encrypted credential is empty. Import this config again.");
                }

                string transport = (profile.Proxy.Transport ?? "tcp").Trim().ToLowerInvariant();
                string[] supportedTransports = new string[] { "tcp", "none", "ws", "grpc", "http", "httpupgrade", "quic", "xhttp" };
                if (!supportedTransports.Contains(transport))
                    throw new InvalidOperationException("This config uses an unsupported transport: " + transport);
                if (transport == "xhttp" && protocol != "vless" && protocol != "vmess" && protocol != "trojan")
                    throw new InvalidOperationException("XHTTP is supported only for VLESS, VMess, and Trojan profiles.");
                if (String.Equals(profile.Proxy.TlsMode, "reality", StringComparison.OrdinalIgnoreCase)
                    && String.IsNullOrWhiteSpace(profile.Proxy.RealityPublicKey))
                    throw new InvalidOperationException("The Reality public key is missing from this config.");
                return String.Empty;
            }
            catch (Exception exception)
            {
                return exception.Message;
            }
        }

        private void RefreshV2RayProfileSelector(string selectedId)
        {
            ComboBox input = Find<ComboBox>("V2RayProfileInput");
            ConnectionProfile current = input.SelectedItem as ConnectionProfile;
            string keepId = !String.IsNullOrWhiteSpace(selectedId)
                ? selectedId
                : current == null ? (!ActiveProfile.IsSsh && !ActiveProfile.IsPsiphon ? ActiveProfileId : null) : current.Id;
            List<ConnectionProfile> profiles = _settings.Profiles.Items
                .Where(delegate(ConnectionProfile profile)
                {
                    return profile != null && !profile.IsSsh && !profile.IsPsiphon;
                })
                .OrderBy(delegate(ConnectionProfile profile)
                {
                    return String.Equals(profile.Id, ActiveProfileId, StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                })
                .ThenBy(delegate(ConnectionProfile profile) { return profile.Name; }, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _refreshingV2RayProfiles = true;
            input.ItemsSource = profiles;
            input.SelectedItem = profiles.FirstOrDefault(delegate(ConnectionProfile profile)
            {
                return String.Equals(profile.Id, keepId, StringComparison.OrdinalIgnoreCase);
            });
            if (input.SelectedItem == null && profiles.Count > 0) input.SelectedIndex = 0;
            input.IsEnabled = !_manager.IsRunning;
            _refreshingV2RayProfiles = false;
            UpdateV2RayProfileSelection();
        }

        private void UpdateV2RayProfileSelection()
        {
            if (_refreshingV2RayProfiles) return;
            ConnectionProfile profile = Find<ComboBox>("V2RayProfileInput").SelectedItem as ConnectionProfile;
            TextBlock status = Find<TextBlock>("V2RayProfileStatusText");
            Button use = Find<Button>("UseV2RayProfileButton");
            if (profile == null)
            {
                status.Text = "No V2Ray profile yet. Add a subscription above or import a config on this page.";
                status.Foreground = Brush("#FFBD69");
                use.IsEnabled = false;
                return;
            }
            string error = GetProxyValidationError(profile);
            bool active = String.Equals(profile.Id, ActiveProfileId, StringComparison.OrdinalIgnoreCase);
            if (!String.IsNullOrEmpty(error))
            {
                status.Text = "Needs repair: " + error;
                status.Foreground = Brush("#FF667A");
                use.Content = "Re-import below";
                use.IsEnabled = !_manager.IsRunning;
            }
            else
            {
                status.Text = profile.ProtocolLabel + "  •  " + profile.EndpointDisplay
                    + (active ? "  •  active" : String.Empty);
                status.Foreground = active ? Brush("#39DBA0") : Brush("#91A1B8");
                use.Content = "Connect now";
                use.IsEnabled = !_manager.IsRunning;
            }
        }

        private void SelectV2RayProfileFromInput()
        {
            if (_refreshingV2RayProfiles) return;
            UpdateV2RayProfileSelection();
            ConnectionProfile profile = Find<ComboBox>("V2RayProfileInput").SelectedItem as ConnectionProfile;
            if (profile == null || _manager.IsRunning
                || String.Equals(profile.Id, ActiveProfileId, StringComparison.OrdinalIgnoreCase)) return;
            if (!String.IsNullOrEmpty(GetProxyValidationError(profile))) return;
            try { ActivateProfileInternal(profile, "V2Ray selection"); }
            catch (Exception exception) { ShowError(exception.Message, "V2Ray profile"); }
        }

        private async Task UseSelectedV2RayProfileAsync()
        {
            ConnectionProfile profile = Find<ComboBox>("V2RayProfileInput").SelectedItem as ConnectionProfile;
            if (profile == null) return;
            string error = GetProxyValidationError(profile);
            if (!String.IsNullOrEmpty(error))
            {
                Find<TextBlock>("ImportResultText").Text = "Repair required: " + error;
                Find<TextBlock>("ImportResultText").Foreground = Brush("#FFBD69");
                Find<TextBox>("ConfigImportInput").Focus();
                return;
            }
            try
            {
                ActivateProfileInternal(profile, "V2Ray selector");
                RefreshV2RayProfileSelector(profile.Id);
                await ConnectAsync();
            }
            catch (Exception exception) { ShowError(exception.Message, "V2Ray profile"); }
        }

        private void BrowseSingBox()
        {
            ShowInfo("Custom connection cores are disabled in secure mode. Nivan runs only the reviewed bundled core.", "Secure core policy");
        }

        private async Task TestSingBoxAsync()
        {
            if (!SaveSingBoxControls(true)) return;
            Button button = Find<Button>("TestSingBoxButton");
            TextBlock status = Find<TextBlock>("SingBoxStatusText");
            string oldText = button.Content == null ? "Test core" : button.Content.ToString();
            button.Content = "Testing...";
            button.IsEnabled = false;
            status.Text = "Checking sing-box executable...";
            status.Foreground = Brush("#5DD9FF");
            try
            {
                string version = await SingBoxConnectionProvider.ReadVersionAsync(
                    _paths,
                    _settings.SingBox.ExecutablePath,
                    _settings.SingBox.UseBundledCore
                );
                string firstLine = version.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                status.Text = String.IsNullOrWhiteSpace(firstLine) ? "sing-box core is ready." : firstLine;
                status.Foreground = Brush("#39DBA0");
                _logger.Info("sing-box core test passed: " + status.Text);
            }
            catch (Exception exception)
            {
                status.Text = "Core check failed: " + exception.Message;
                status.Foreground = Brush("#FF667A");
                ShowError(exception.Message, "sing-box core test");
            }
            finally
            {
                button.Content = oldText;
                button.IsEnabled = true;
            }
        }

        private void ImportConfigFile()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "Import V2Ray share links or subscription text";
            dialog.Filter = "Config text|*.txt;*.conf;*.json|All files|*.*";
            if (dialog.ShowDialog(_window) != true) return;
            try
            {
                string content = File.ReadAllText(dialog.FileName);
                Find<TextBox>("ConfigImportInput").Text = content;
                ImportProxyText(content, Find<TextBox>("ImportCategoryInput").Text);
            }
            catch (Exception exception)
            {
                ShowError("The config file could not be read.\n\n" + exception.Message, "Import configs");
            }
        }

        private async Task ImportQrImageAsync()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "Import an offline QR config image";
            dialog.Filter = "QR images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*";
            if (dialog.ShowDialog(_window) != true) return;

            Button button = Find<Button>("ImportQrConfigButton");
            string oldText = button.Content == null ? "Import QR image..." : button.Content.ToString();
            button.Content = "Reading QR...";
            button.IsEnabled = false;
            try
            {
                IList<QrPayload> payloads = _qrDecoder.Decode(dialog.FileName);
                List<string> shareConfigs = new List<string>();
                List<Uri> subscriptions = new List<Uri>();
                List<string> unsupported = new List<string>();

                foreach (QrPayload payload in payloads)
                {
                    string text = (payload.Text ?? String.Empty).Trim().Trim('\uFEFF');
                    if (ContainsSupportedShareLink(text))
                    {
                        shareConfigs.Add(text);
                        continue;
                    }

                    Uri uri;
                    if (Uri.TryCreate(text, UriKind.Absolute, out uri)
                        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
                    {
                        subscriptions.Add(uri);
                        continue;
                    }

                    unsupported.Add(DescribeQrPayload(text));
                }

                int imported = 0;
                string configDetail = String.Empty;
                if (shareConfigs.Count > 0)
                {
                    string content = String.Join(Environment.NewLine, shareConfigs.ToArray());
                    Find<TextBox>("ConfigImportInput").Text = content;
                    imported += ImportProxyText(content, Find<TextBox>("ImportCategoryInput").Text);
                    configDetail = Find<TextBlock>("ImportResultText").Text;
                }

                int subscriptionCount = 0;
                foreach (Uri subscription in subscriptions.Take(10))
                {
                    Find<TextBox>("SubscriptionNameInput").Text = subscription.Host;
                    Find<TextBox>("SubscriptionUrlInput").Text = subscription.AbsoluteUri;
                    Find<TextBox>("SubscriptionCategoryInput").Text = "QR Subscription";
                    int countBefore = _settings.Subscriptions.Items.Count;
                    await DownloadSubscriptionAsync();
                    if (_settings.Subscriptions.Items.Count > countBefore) subscriptionCount++;
                }

                string summary = "QR recognized: " + imported + " configs imported";
                if (subscriptionCount > 0) summary += "  •  " + subscriptionCount + " subscriptions added";
                if (unsupported.Count > 0)
                    summary += "  •  unsupported: " + String.Join(", ", unsupported.Take(3).ToArray());
                if (!String.IsNullOrWhiteSpace(configDetail)) summary += Environment.NewLine + configDetail;
                Find<TextBlock>("ImportResultText").Text = summary;
                Find<TextBlock>("ImportResultText").Foreground = imported > 0 || subscriptionCount > 0
                    ? Brush("#39DBA0") : Brush("#FFBD69");
                if (imported == 0 && subscriptionCount == 0)
                    ShowError(
                        "The QR code was readable, but it does not contain a supported V2Ray share link or an HTTP/HTTPS subscription.",
                        "Unsupported QR content"
                    );
                _logger.Info("Offline QR import completed without sending the image or payload to a remote service.");
            }
            catch (Exception exception)
            {
                _logger.Warning("Offline QR import failed: " + exception.Message);
                ShowError(exception.Message, "QR import");
            }
            finally
            {
                button.Content = oldText;
                button.IsEnabled = true;
            }
        }

        private static bool ContainsSupportedShareLink(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return false;
            string[] schemes = new string[] { "vmess://", "vless://", "trojan://", "ss://" };
            return schemes.Any(delegate(string scheme)
            {
                return value.IndexOf(scheme, StringComparison.OrdinalIgnoreCase) >= 0;
            });
        }

        private static string DescribeQrPayload(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return "empty";
            int separator = value.IndexOf("://", StringComparison.Ordinal);
            if (separator > 0 && separator < 32)
                return value.Substring(0, separator).ToLowerInvariant() + "://";
            if (value.TrimStart().StartsWith("{", StringComparison.Ordinal)) return "JSON";
            return "text";
        }

        private int ImportProxyText(string text, string category)
        {
            return ImportProxyText(text, category, null);
        }

        private int ImportProxyText(string text, string category, string subscriptionId)
        {
            ProxyImportResult parsed = _configImporter.ParseMany(text, category);
            Dictionary<string, ConnectionProfile> existing = new Dictionary<string, ConnectionProfile>(
                StringComparer.OrdinalIgnoreCase
            );
            foreach (ConnectionProfile savedProfile in _settings.Profiles.Items)
            {
                if (!savedProfile.IsSsh && savedProfile.Proxy != null
                    && !String.IsNullOrWhiteSpace(savedProfile.Proxy.ImportFingerprint)
                    && !existing.ContainsKey(savedProfile.Proxy.ImportFingerprint))
                    existing.Add(savedProfile.Proxy.ImportFingerprint, savedProfile);
            }
            List<ConnectionProfile> added = new List<ConnectionProfile>();
            ConnectionProfile readyProfile = null;
            int duplicateCount = 0;
            int repairedCount = 0;
            try
            {
                foreach (ImportedProxyProfile imported in parsed.Profiles)
                {
                    ConnectionProfile profile = imported.Profile;
                    ConnectionProfile existingProfile;
                    if (existing.TryGetValue(profile.Proxy.ImportFingerprint, out existingProfile))
                    {
                        try
                        {
                            // Re-import is also the recovery path for credentials that were
                            // deleted or copied from another Windows DPAPI account.
                            _proxySecrets.Save(existingProfile.Id, imported.Secret);
                            int localPort = existingProfile.Proxy.LocalSocksPort;
                            existingProfile.Proxy = profile.Proxy;
                            existingProfile.Proxy.LocalSocksPort = localPort > 0 ? localPort : FindNextSocksPort(1081);
                            existingProfile.Engine = profile.Engine;
                            if (!String.IsNullOrWhiteSpace(subscriptionId))
                                existingProfile.SubscriptionId = subscriptionId;
                            repairedCount++;
                            if (readyProfile == null && String.IsNullOrEmpty(GetProxyValidationError(existingProfile)))
                                readyProfile = existingProfile;
                        }
                        catch (Exception exception)
                        {
                            parsed.Errors.Add(existingProfile.Name + ": credential repair failed: " + exception.Message);
                        }
                        duplicateCount++;
                        continue;
                    }
                    try
                    {
                        profile.Name = UniqueProfileName(profile.Name);
                        profile.Proxy.LocalSocksPort = FindNextSocksPort(1081);
                        profile.SubscriptionId = subscriptionId ?? String.Empty;
                        _proxySecrets.Save(profile.Id, imported.Secret);
                        _settings.Profiles.Items.Add(profile);
                        added.Add(profile);
                        if (readyProfile == null) readyProfile = profile;
                        existing.Add(profile.Proxy.ImportFingerprint, profile);
                    }
                    catch (Exception exception)
                    {
                        _proxySecrets.Delete(profile.Id);
                        parsed.Errors.Add(profile.Name + ": " + exception.Message);
                    }
                }

                if (added.Count > 0 || repairedCount > 0 || !String.IsNullOrWhiteSpace(subscriptionId))
                {
                    _settingsService.Save(_settings);
                    RefreshProfileFilters();
                    SelectComboText(Find<ComboBox>("ProfileCategoryFilter"), "All categories");
                    RefreshProfileList(added.Count > 0 ? added[0].Id : ActiveProfileId);
                    RefreshV2RayProfileSelector(readyProfile == null ? null : readyProfile.Id);
                }
                if (readyProfile != null && !_manager.IsRunning)
                    ActivateProfileInternal(readyProfile, "V2Ray import");
            }
            catch (Exception exception)
            {
                foreach (ConnectionProfile profile in added)
                {
                    _settings.Profiles.Items.Remove(profile);
                    _proxySecrets.Delete(profile.Id);
                }
                ShowError(exception.Message, "Import configs");
                return 0;
            }

            int errorCount = parsed.Errors.Count;
            string summary = added.Count + " imported  •  " + repairedCount + " repaired  •  "
                + duplicateCount + " duplicates  •  " + errorCount + " errors";
            if (errorCount > 0)
            {
                string details = String.Join(Environment.NewLine, parsed.Errors.Take(3).ToArray());
                summary += Environment.NewLine + details;
            }
            if (readyProfile != null)
                summary += Environment.NewLine + "Ready: " + readyProfile.Name + ". Press Connect on this page.";
            TextBlock result = Find<TextBlock>("ImportResultText");
            result.Text = summary;
            result.Foreground = added.Count > 0 ? Brush("#39DBA0") : errorCount > 0 ? Brush("#FF667A") : Brush("#FFBD69");
            _logger.Info("Proxy import completed: " + added.Count + " imported, " + repairedCount
                + " repaired, " + duplicateCount + " duplicates, " + errorCount + " errors.");
            if (added.Count == 0 && errorCount > 0)
                ShowError(summary, "Import configs");
            return added.Count;
        }

        private void AddExternalProxy()
        {
            try
            {
                int port;
                if (!Int32.TryParse(Find<TextBox>("ExternalProxyPortInput").Text.Trim(), out port))
                    throw new InvalidOperationException("Enter a valid external proxy port.");
                string protocol = SelectedTag(Find<ComboBox>("ExternalProxyProtocolInput"));
                int localPort = FindNextSocksPort(1100);
                ConnectionProfile profile = _externalProxy.Create(
                    Find<TextBox>("ExternalProxyNameInput").Text,
                    protocol,
                    Find<TextBox>("ExternalProxyHostInput").Text,
                    port,
                    Find<TextBox>("ExternalProxyUsernameInput").Text,
                    Find<PasswordBox>("ExternalProxyPasswordInput").Password,
                    localPort
                );
                Find<PasswordBox>("ExternalProxyPasswordInput").Clear();
                _settings.Profiles.Items.Add(profile);
                _settings.Profiles.ActiveProfileId = profile.Id;
                _settingsService.Save(_settings);
                RefreshProfileFilters();
                RefreshProfileList(profile.Id);
                UpdateEndpointLabels();
                Find<TextBlock>("ExternalProxyStatusText").Text = "External proxy added. Test it before sending sensitive traffic.";
                Find<TextBlock>("ExternalProxyStatusText").Foreground = Brush("#39DBA0");
                _logger.Info("External proxy profile added: " + profile.Name + ".");
                ShowPage("Dashboard");
            }
            catch (Exception exception)
            {
                Find<TextBlock>("ExternalProxyStatusText").Text = exception.Message;
                Find<TextBlock>("ExternalProxyStatusText").Foreground = Brush("#FF667A");
                ShowError(exception.Message, "External proxy");
            }
        }

        private async Task BrowsePsiphonCoreAsync()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "Select the official Psiphon executable";
            dialog.Filter = "Psiphon ConsoleClient|ConsoleClient.exe;psiphon3.exe|Windows executable|*.exe";
            if (dialog.ShowDialog(_window) != true) return;
            try
            {
                Find<TextBlock>("PsiphonStatusText").Text = "Verifying the Windows publisher signature...";
                await _integrity.VerifyPsiphonPublisherAsync(dialog.FileName);
                string fingerprint = _integrity.ComputeSha256(dialog.FileName);
                _settings.Psiphon.ExecutablePath = dialog.FileName;
                _settings.Psiphon.ApprovedExecutableSha256 = fingerprint;
                Find<TextBox>("PsiphonCorePathInput").Text = dialog.FileName;
                _settingsService.Save(_settings);
                UpdatePsiphonStatus();
                _logger.Info("Official Psiphon executable approved and fingerprint pinned.");
            }
            catch (Exception exception)
            {
                Find<TextBlock>("PsiphonStatusText").Text = exception.Message;
                Find<TextBlock>("PsiphonStatusText").Foreground = Brush("#FF667A");
                ShowError(exception.Message, "Psiphon security check");
            }
        }

        private void BrowsePsiphonConfig()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "Select the official Psiphon client.config";
            dialog.Filter = "Psiphon config|client.config;*.json|All files|*.*";
            if (dialog.ShowDialog(_window) != true) return;
            try
            {
                FileInfo file = new FileInfo(dialog.FileName);
                if (file.Length <= 0 || file.Length > 1024 * 1024)
                    throw new InvalidOperationException("The Psiphon config must be valid JSON smaller than 1 MB.");
                if (!String.Equals(System.IO.Path.GetFileName(dialog.FileName), "client.config", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Select the official file named client.config.");
                string beginning;
                using (StreamReader reader = new StreamReader(dialog.FileName, Encoding.UTF8, true))
                    beginning = (reader.ReadLine() ?? String.Empty).Trim().TrimStart('\uFEFF');
                if (!beginning.StartsWith("{", StringComparison.Ordinal))
                    throw new InvalidOperationException("The selected Psiphon config is not a JSON object.");
                _settings.Psiphon.ConfigPath = dialog.FileName;
                _settings.Psiphon.ApprovedConfigSha256 = _integrity.ComputeFileSha256(dialog.FileName);
                Find<TextBox>("PsiphonConfigPathInput").Text = dialog.FileName;
                _settingsService.Save(_settings);
                UpdatePsiphonStatus();
            }
            catch (Exception exception) { ShowError(exception.Message, "Psiphon config"); }
        }

        private bool SavePsiphonControls(bool showErrors)
        {
            try
            {
                string executable = Find<TextBox>("PsiphonCorePathInput").Text.Trim();
                string config = Find<TextBox>("PsiphonConfigPathInput").Text.Trim();
                if (String.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
                    throw new InvalidOperationException("Select an official Psiphon executable first.");
                if (String.IsNullOrWhiteSpace(config) || !File.Exists(config))
                    throw new InvalidOperationException("Select the official Psiphon client.config file.");
                if (!String.Equals(System.IO.Path.GetFileName(config), "client.config", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The Psiphon configuration file must be named client.config.");
                _integrity.VerifyPinned(executable, _settings.Psiphon.ApprovedExecutableSha256, "The Psiphon core");
                _integrity.VerifyFilePinned(config, _settings.Psiphon.ApprovedConfigSha256, "The Psiphon client config");
                int socks = ReadInteger("PsiphonSocksPortInput", "Psiphon SOCKS port", 1024, 65535);
                int http = ReadInteger("PsiphonHttpPortInput", "Psiphon HTTP port", 1024, 65535);
                if (socks == http || socks == _settings.NekoRay.MixedPort || http == _settings.NekoRay.MixedPort)
                    throw new InvalidOperationException("Psiphon and integrated routing ports must be different.");
                string region = Find<TextBox>("PsiphonRegionInput").Text.Trim().ToUpperInvariant();
                if (region.Length > 0 && !Regex.IsMatch(region, "^[A-Z]{2}$"))
                    throw new InvalidOperationException("Psiphon region must be a two-letter country code, such as DE.");
                _settings.Psiphon.Enabled = true;
                _settings.Psiphon.ExecutablePath = executable;
                _settings.Psiphon.ConfigPath = config;
                _settings.Psiphon.LocalSocksPort = socks;
                _settings.Psiphon.LocalHttpPort = http;
                _settings.Psiphon.Region = region;
                _settings.Psiphon.AutoReconnect = Find<CheckBox>("PsiphonAutoReconnectCheck").IsChecked == true;
                _settings.Psiphon.ReconnectDelaySeconds = ReadInteger("PsiphonReconnectDelayInput", "Psiphon restart delay", 2, 300);
                foreach (ConnectionProfile profile in _settings.Profiles.Items.Where(delegate(ConnectionProfile item) { return item.IsPsiphon; }))
                {
                    if (profile.Psiphon == null) profile.Psiphon = new PsiphonProfileSettings();
                    profile.Psiphon.LocalSocksPort = socks;
                    profile.Psiphon.LocalHttpPort = http;
                    profile.Psiphon.Region = region;
                }
                _settingsService.Save(_settings);
                UpdatePsiphonStatus();
                return true;
            }
            catch (Exception exception)
            {
                if (showErrors) ShowError(exception.Message, "Psiphon settings");
                return false;
            }
        }

        private async Task TestPsiphonAsync()
        {
            if (!SavePsiphonControls(true)) return;
            try
            {
                Find<TextBlock>("PsiphonStatusText").Text = "Verifying official Psiphon signature and pinned fingerprint...";
                await _integrity.VerifyPsiphonPublisherAsync(_settings.Psiphon.ExecutablePath);
                _integrity.VerifyPinned(_settings.Psiphon.ExecutablePath, _settings.Psiphon.ApprovedExecutableSha256, "The Psiphon core");
                _integrity.VerifyFilePinned(_settings.Psiphon.ConfigPath, _settings.Psiphon.ApprovedConfigSha256, "The Psiphon client config");
                Find<TextBlock>("PsiphonStatusText").Text = "Official Psiphon files verified. The provider is ready.";
                Find<TextBlock>("PsiphonStatusText").Foreground = Brush("#39DBA0");
            }
            catch (Exception exception)
            {
                Find<TextBlock>("PsiphonStatusText").Text = exception.Message;
                Find<TextBlock>("PsiphonStatusText").Foreground = Brush("#FF667A");
                ShowError(exception.Message, "Psiphon verification");
            }
        }

        private void CreatePsiphonProfile()
        {
            if (!SavePsiphonControls(true)) return;
            if (_manager.IsRunning)
            {
                ShowError("Disconnect before switching to Psiphon.");
                return;
            }
            ConnectionProfile profile = _settings.Profiles.Items.FirstOrDefault(delegate(ConnectionProfile item) { return item.IsPsiphon; });
            if (profile == null)
            {
                profile = new ConnectionProfile
                {
                    Id = "psiphon-" + Guid.NewGuid().ToString("N"),
                    Name = "Psiphon Free",
                    Category = "Free fallback",
                    Engine = "psiphon",
                    IsFavorite = false,
                    Psiphon = new PsiphonProfileSettings
                    {
                        LocalSocksPort = _settings.Psiphon.LocalSocksPort,
                        LocalHttpPort = _settings.Psiphon.LocalHttpPort,
                        Region = _settings.Psiphon.Region
                    },
                    LastLatencyMilliseconds = -1,
                    LastTestStatus = "Provider ready",
                    SubscriptionId = String.Empty
                };
                _settings.Profiles.Items.Add(profile);
            }
            _settings.Profiles.ActiveProfileId = profile.Id;
            _settingsService.Save(_settings);
            RefreshProfileFilters();
            RefreshProfileList(profile.Id);
            UpdateEndpointLabels();
            UpdateNekoVisual();
            ShowPage("Dashboard");
            _logger.Info("Psiphon provider profile activated.");
        }

        private void UpdatePsiphonStatus()
        {
            string hash = _settings.Psiphon.ApprovedExecutableSha256 ?? String.Empty;
            string configHash = _settings.Psiphon.ApprovedConfigSha256 ?? String.Empty;
            Find<TextBlock>("PsiphonHashText").Text = hash.Length == 64
                ? "Core SHA-256: " + hash + (configHash.Length == 64 ? "\nConfig SHA-256: " + configHash : "")
                : "No Psiphon executable has been approved.";
            bool ready = File.Exists(_settings.Psiphon.ExecutablePath) && File.Exists(_settings.Psiphon.ConfigPath)
                && hash.Length == 64 && configHash.Length == 64;
            Find<TextBlock>("PsiphonStatusText").Text = ready
                ? "Files selected. Use Verify files before the first connection."
                : "Psiphon is not configured yet.";
            Find<TextBlock>("PsiphonStatusText").Foreground = ready ? Brush("#5DD9FF") : Brush("#74859D");
        }

        private void RefreshDnsProviders(string selectedId)
        {
            _dnsProviders = _dns.GetProviders(_settings.Dns);
            ComboBox input = Find<ComboBox>("DnsProviderInput");
            input.ItemsSource = _dnsProviders;
            input.SelectedItem = _dnsProviders.FirstOrDefault(delegate(DnsProviderInfo item)
            {
                return String.Equals(item.Id, selectedId, StringComparison.OrdinalIgnoreCase);
            }) ?? _dnsProviders[0];
            Find<Button>("RestoreDnsButton").IsEnabled = _dns.HasPendingRestore;
            UpdateHomeDnsButton();
        }

        private void UpdateHomeDnsButton()
        {
            DnsProviderInfo active = _dnsProviders == null ? null : _dnsProviders.FirstOrDefault(delegate(DnsProviderInfo item)
            {
                return String.Equals(item.Id, _settings.Dns.ActiveProviderId, StringComparison.OrdinalIgnoreCase);
            });
            Find<TextBlock>("HomeDnsStatusText").Text = active == null || active.Id == "automatic"
                ? "Automatic"
                : active.Name;
        }

        private DnsProviderInfo SelectedDnsProvider
        {
            get { return Find<ComboBox>("DnsProviderInput").SelectedItem as DnsProviderInfo; }
        }

        private async Task ApplySelectedDnsAsync()
        {
            if (_dnsBusy) return;
            DnsProviderInfo provider = SelectedDnsProvider;
            if (provider == null) return;
            SetDnsBusy(true);
            try
            {
                await _dns.ApplyAsync(provider);
                _settings.Dns.ActiveProviderId = provider.Id;
                _settingsService.Save(_settings);
                UpdateHomeDnsButton();
                Find<TextBlock>("DnsStatusText").Text = provider.Id == "automatic"
                    ? "Windows automatic DNS restored."
                    : provider.Name + " is active on connected network adapters.";
                Find<TextBlock>("DnsStatusText").Foreground = Brush("#39DBA0");
                Find<Button>("RestoreDnsButton").IsEnabled = _dns.HasPendingRestore;
            }
            catch (Exception exception)
            {
                Find<TextBlock>("DnsStatusText").Text = "DNS change failed: " + exception.Message;
                Find<TextBlock>("DnsStatusText").Foreground = Brush("#FF667A");
                ShowError(exception.Message, "DNS Center");
            }
            finally { SetDnsBusy(false); }
        }

        private async Task TestSelectedDnsAsync()
        {
            if (_dnsBusy || SelectedDnsProvider == null) return;
            SetDnsBusy(true);
            try
            {
                DnsProbeResult result = await _dns.TestAsync(SelectedDnsProvider);
                Find<TextBox>("DnsResultsBox").Text = FormatDnsResult(result);
            }
            finally { SetDnsBusy(false); }
        }

        private async Task TestAllDnsAsync()
        {
            if (_dnsBusy) return;
            SetDnsBusy(true);
            Find<TextBox>("DnsResultsBox").Text = "Testing DNS providers...";
            try
            {
                DnsProviderInfo[] providers = _dnsProviders.Where(delegate(DnsProviderInfo item) { return item.Id != "automatic"; }).ToArray();
                DnsProbeResult[] results = await Task.WhenAll(providers.Select(delegate(DnsProviderInfo item) { return _dns.TestAsync(item); }));
                Find<TextBox>("DnsResultsBox").Text = String.Join(Environment.NewLine,
                    results.OrderBy(delegate(DnsProbeResult item) { return item.Success ? 0 : 1; })
                        .ThenBy(delegate(DnsProbeResult item) { return item.Milliseconds; })
                        .Select(FormatDnsResult));
            }
            finally { SetDnsBusy(false); }
        }

        private async Task RestoreDnsAsync()
        {
            if (_dnsBusy) return;
            SetDnsBusy(true);
            try
            {
                await _dns.RestoreAsync();
                _settings.Dns.ActiveProviderId = "automatic";
                _settingsService.Save(_settings);
                RefreshDnsProviders("automatic");
                Find<TextBlock>("DnsStatusText").Text = "Previous Windows DNS settings restored.";
                Find<TextBlock>("DnsStatusText").Foreground = Brush("#39DBA0");
            }
            catch (Exception exception) { ShowError(exception.Message, "Restore DNS"); }
            finally { SetDnsBusy(false); }
        }

        private void SaveCustomDns()
        {
            string primary = Find<TextBox>("CustomDnsPrimaryInput").Text.Trim();
            string secondary = Find<TextBox>("CustomDnsSecondaryInput").Text.Trim();
            System.Net.IPAddress parsed;
            if (!System.Net.IPAddress.TryParse(primary, out parsed)
                || parsed.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
                || (!String.IsNullOrWhiteSpace(secondary)
                    && (!System.Net.IPAddress.TryParse(secondary, out parsed)
                        || parsed.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)))
            {
                ShowError("Enter valid IPv4 addresses for the custom DNS.", "Custom DNS");
                return;
            }
            _settings.Dns.CustomName = Find<TextBox>("CustomDnsNameInput").Text.Trim();
            _settings.Dns.CustomPrimary = primary;
            _settings.Dns.CustomSecondary = secondary;
            _settingsService.Save(_settings);
            RefreshDnsProviders("custom");
            Find<TextBlock>("DnsStatusText").Text = "Custom DNS saved. Use Test selected before applying it.";
        }

        private void SaveDnsPreferences()
        {
            _settings.Dns.RestoreOnDisconnect = Find<CheckBox>("DnsRestoreOnDisconnectCheck").IsChecked == true;
            _settings.Dns.RestoreAfterCrash = Find<CheckBox>("DnsRestoreAfterCrashCheck").IsChecked == true;
            _settingsService.Save(_settings);
        }

        private void SetDnsBusy(bool busy)
        {
            _dnsBusy = busy;
            Find<Button>("ApplyDnsButton").IsEnabled = !busy;
            Find<Button>("TestDnsButton").IsEnabled = !busy;
            Find<Button>("TestAllDnsButton").IsEnabled = !busy;
            Find<Button>("RestoreDnsButton").IsEnabled = !busy && _dns.HasPendingRestore;
        }

        private static string FormatDnsResult(DnsProbeResult result)
        {
            return (result.Success ? "✓ " : "✕ ") + result.Provider.Name + "  •  "
                + (result.Success ? result.Milliseconds + " ms" : result.Detail);
        }

        private async Task DownloadSubscriptionAsync()
        {
            if (_subscriptionBusy) return;
            Button button = Find<Button>("DownloadSubscriptionButton");
            TextBlock result = Find<TextBlock>("ImportResultText");
            string oldText = button.Content == null ? "Save & import now" : button.Content.ToString();
            string address = Find<TextBox>("SubscriptionUrlInput").Text.Trim();
            string category = Find<TextBox>("SubscriptionCategoryInput").Text.Trim();
            string name = Find<TextBox>("SubscriptionNameInput").Text.Trim();
            Uri uri;
            if (!Uri.TryCreate(address, UriKind.Absolute, out uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                ShowError("Enter a valid HTTP or HTTPS subscription URL.", "Managed subscription");
                return;
            }
            if (String.IsNullOrWhiteSpace(category)) category = "Subscription";
            if (String.IsNullOrWhiteSpace(name)) name = uri.Host;

            SubscriptionEntry subscription = new SubscriptionEntry
            {
                Id = "subscription-" + Guid.NewGuid().ToString("N"),
                Name = name,
                Category = category,
                AutoUpdate = Find<CheckBox>("SubscriptionAutoUpdateCheck").IsChecked == true,
                RefreshIntervalHours = 24,
                LastStatus = "Downloading",
                ProfileCount = 0
            };
            button.Content = "Saving...";
            SetSubscriptionBusy(true);
            result.Text = "Downloading subscription through HTTPS...";
            result.Foreground = Brush("#5DD9FF");
            try
            {
                _subscriptionSecrets.Save(subscription.Id, address);
                string content = await _subscriptions.DownloadAsync(address, _settings.Security);
                int imported = ImportProxyText(content, subscription.Category, subscription.Id);
                subscription.ProfileCount = _settings.Profiles.Items.Count(delegate(ConnectionProfile profile)
                {
                    return String.Equals(profile.SubscriptionId, subscription.Id, StringComparison.OrdinalIgnoreCase);
                });
                if (subscription.ProfileCount == 0)
                    throw new InvalidOperationException(
                        "The subscription did not contain a compatible VMess, VLESS, Trojan, or Shadowsocks config."
                    );
                subscription.LastUpdatedUtc = DateTime.UtcNow;
                subscription.LastStatus = "Updated • " + imported + " new";
                _settings.Subscriptions.Items.Add(subscription);
                _settingsService.Save(_settings);
                RefreshSubscriptionList(subscription.Id);
                result.Text = "Subscription added. " + subscription.ProfileCount
                    + " profiles are ready; choose one and press Connect or double-click it. No refresh or profile save is required.";
                result.Foreground = Brush("#39DBA0");
                Find<TextBox>("SubscriptionNameInput").Clear();
                Find<TextBox>("SubscriptionUrlInput").Clear();
                _logger.Info("Managed subscription added: " + subscription.Name + ".");
            }
            catch (Exception exception)
            {
                _settings.Subscriptions.Items.Remove(subscription);
                try { _subscriptionSecrets.Delete(subscription.Id); } catch { }
                result.Text = "Subscription failed: " + exception.Message;
                result.Foreground = Brush("#FF667A");
                ShowError(exception.Message, "Subscription import");
            }
            finally
            {
                button.Content = oldText;
                SetSubscriptionBusy(false);
            }
        }

        private async Task RefreshSelectedSubscriptionAsync()
        {
            SubscriptionEntry subscription = SelectedSubscription;
            if (subscription == null || _subscriptionBusy) return;
            SetSubscriptionBusy(true);
            try { await RefreshSubscriptionAsync(subscription, true); }
            finally { SetSubscriptionBusy(false); }
        }

        private async Task RefreshAllSubscriptionsAsync(bool showResult)
        {
            if (_subscriptionBusy) return;
            List<SubscriptionEntry> subscriptions = _settings.Subscriptions.Items.ToList();
            if (subscriptions.Count == 0)
            {
                if (showResult) ShowInfo("No managed subscriptions have been saved.", "Subscriptions");
                return;
            }
            SetSubscriptionBusy(true);
            int succeeded = 0;
            try
            {
                foreach (SubscriptionEntry subscription in subscriptions)
                {
                    if (await RefreshSubscriptionAsync(subscription, false)) succeeded++;
                }
                if (showResult)
                    ShowInfo(succeeded + " of " + subscriptions.Count + " subscriptions refreshed.", "Subscriptions");
            }
            finally { SetSubscriptionBusy(false); }
        }

        private async Task<bool> RefreshSubscriptionAsync(SubscriptionEntry subscription, bool showError)
        {
            try
            {
                Find<TextBlock>("SubscriptionStatusText").Text = "Refreshing " + subscription.Name + "...";
                string address = _subscriptionSecrets.Read(subscription.Id);
                string content = await _subscriptions.DownloadAsync(address, _settings.Security);
                int imported = ImportProxyText(content, subscription.Category, subscription.Id);
                subscription.ProfileCount = _settings.Profiles.Items.Count(delegate(ConnectionProfile profile)
                {
                    return String.Equals(profile.SubscriptionId, subscription.Id, StringComparison.OrdinalIgnoreCase);
                });
                subscription.LastUpdatedUtc = DateTime.UtcNow;
                subscription.LastStatus = "Updated • " + imported + " new";
                _settingsService.Save(_settings);
                RefreshSubscriptionList(subscription.Id);
                _logger.Info("Subscription refreshed: " + subscription.Name + ".");
                return true;
            }
            catch (Exception exception)
            {
                subscription.LastStatus = "Refresh failed";
                _settingsService.Save(_settings);
                RefreshSubscriptionList(subscription.Id);
                _logger.Warning("Subscription refresh failed for " + subscription.Name + ": " + exception.Message);
                if (showError) ShowError(exception.Message, "Refresh subscription");
                return false;
            }
        }

        private void RefreshSubscriptionList(string selectedId)
        {
            string keepId = selectedId;
            if (String.IsNullOrWhiteSpace(keepId) && SelectedSubscription != null)
                keepId = SelectedSubscription.Id;
            _subscriptionRows.Clear();
            SubscriptionRowViewModel selectedRow = null;
            foreach (SubscriptionEntry subscription in _settings.Subscriptions.Items
                .OrderBy(delegate(SubscriptionEntry item) { return item.Name; }))
            {
                subscription.ProfileCount = _settings.Profiles.Items.Count(delegate(ConnectionProfile profile)
                {
                    return String.Equals(profile.SubscriptionId, subscription.Id, StringComparison.OrdinalIgnoreCase);
                });
                SubscriptionRowViewModel row = new SubscriptionRowViewModel(subscription);
                _subscriptionRows.Add(row);
                if (String.Equals(subscription.Id, keepId, StringComparison.OrdinalIgnoreCase)) selectedRow = row;
            }
            Find<ListBox>("SubscriptionList").SelectedItem = selectedRow;
            if (selectedRow == null && _subscriptionRows.Count > 0)
                Find<ListBox>("SubscriptionList").SelectedIndex = 0;
            UpdateSubscriptionSelection();
        }

        private void UpdateSubscriptionSelection()
        {
            SubscriptionEntry subscription = SelectedSubscription;
            Find<Button>("RefreshSelectedSubscriptionButton").IsEnabled = subscription != null && !_subscriptionBusy;
            Find<Button>("RemoveSubscriptionButton").IsEnabled = subscription != null && !_subscriptionBusy;
            Find<TextBlock>("SubscriptionStatusText").Text = subscription == null
                ? "No managed subscription selected."
                : subscription.Name + "  •  " + subscription.ProfileCount + " profiles  •  "
                    + (subscription.LastStatus ?? "Not updated");
        }

        private void SetSubscriptionBusy(bool busy)
        {
            _subscriptionBusy = busy;
            Find<Button>("DownloadSubscriptionButton").IsEnabled = !busy;
            Find<Button>("RefreshAllSubscriptionsButton").IsEnabled = !busy
                && _settings.Subscriptions.Items.Count > 0;
            UpdateSubscriptionSelection();
        }

        private void RemoveSelectedSubscription()
        {
            SubscriptionEntry subscription = SelectedSubscription;
            if (subscription == null || _subscriptionBusy) return;
            if (!Confirm(
                "Remove the managed subscription '" + subscription.Name
                + "'? Imported profiles will be kept as normal local profiles.",
                "Remove subscription"
            )) return;

            foreach (ConnectionProfile profile in _settings.Profiles.Items)
            {
                if (String.Equals(profile.SubscriptionId, subscription.Id, StringComparison.OrdinalIgnoreCase))
                    profile.SubscriptionId = String.Empty;
            }
            _settings.Subscriptions.Items.Remove(subscription);
            _subscriptionSecrets.Delete(subscription.Id);
            _settingsService.Save(_settings);
            RefreshSubscriptionList(null);
            _logger.Info("Managed subscription removed while keeping its imported profiles: " + subscription.Name + ".");
        }

        private async void BeginBackgroundMaintenance()
        {
            await Task.Delay(1200);
            if (_disposed) return;
            List<SubscriptionEntry> due = _settings.Subscriptions.Items
                .Where(delegate(SubscriptionEntry subscription)
                {
                    if (!subscription.AutoUpdate || !_subscriptionSecrets.Exists(subscription.Id)) return false;
                    return !subscription.LastUpdatedUtc.HasValue
                        || DateTime.UtcNow - subscription.LastUpdatedUtc.Value
                            >= TimeSpan.FromHours(Math.Max(1, subscription.RefreshIntervalHours));
                })
                .ToList();
            if (due.Count > 0 && !_subscriptionBusy)
            {
                SetSubscriptionBusy(true);
                try
                {
                    foreach (SubscriptionEntry subscription in due)
                        await RefreshSubscriptionAsync(subscription, false);
                }
                finally { SetSubscriptionBusy(false); }
            }
            if (_settings.Updates.CheckOnStartup
                && !String.IsNullOrWhiteSpace(_settings.Updates.ManifestUrl)
                && !_disposed)
                await CheckForUpdatesAsync(false);
        }

        private async Task CheckForUpdatesAsync(bool showResult)
        {
            if (_updateBusy) return;
            string address = Find<TextBox>("UpdateManifestUrlInput").Text.Trim();
            if (String.IsNullOrWhiteSpace(address))
            {
                if (showResult) ShowError("Enter the vendor's HTTPS update manifest URL first.", "Application updates");
                return;
            }

            SetUpdateBusy(true);
            Find<TextBlock>("UpdateStatusText").Text = "Checking for updates...";
            Find<TextBlock>("UpdateStatusText").Foreground = Brush("#5DD9FF");
            Find<ProgressBar>("UpdateProgressBar").Value = 12;
            try
            {
                _settings.Updates.ManifestUrl = address;
                AppUpdateInfo info = await _appUpdates.CheckAsync(address, CurrentVersion);
                _settings.Updates.LastCheckedUtc = DateTime.UtcNow;
                _availableUpdate = info.IsNewer ? info : null;
                if (info.IsNewer)
                {
                    _settings.Updates.LastStatus = "Version " + info.Version + " is available";
                    Find<TextBlock>("UpdateStatusText").Text = _settings.Updates.LastStatus
                        + (String.IsNullOrWhiteSpace(info.Notes) ? String.Empty : "  •  " + info.Notes);
                    Find<TextBlock>("UpdateStatusText").Foreground = Brush("#39DBA0");
                    Find<ProgressBar>("UpdateProgressBar").Value = 100;
                    if (showResult) ShowInfo("Nivan Shield " + info.Version + " is available.", "Application updates");
                }
                else
                {
                    _settings.Updates.LastStatus = "Nivan Shield is up to date (" + CurrentVersion + ")";
                    Find<TextBlock>("UpdateStatusText").Text = _settings.Updates.LastStatus;
                    Find<TextBlock>("UpdateStatusText").Foreground = Brush("#39DBA0");
                    Find<ProgressBar>("UpdateProgressBar").Value = 100;
                    if (showResult) ShowInfo(_settings.Updates.LastStatus + ".", "Application updates");
                }
                _settingsService.Save(_settings);
            }
            catch (Exception exception)
            {
                _availableUpdate = null;
                _settings.Updates.LastCheckedUtc = DateTime.UtcNow;
                _settings.Updates.LastStatus = "Update check failed";
                _settingsService.Save(_settings);
                Find<TextBlock>("UpdateStatusText").Text = "Update check failed: " + exception.Message;
                Find<TextBlock>("UpdateStatusText").Foreground = Brush("#FF667A");
                Find<ProgressBar>("UpdateProgressBar").Value = 0;
                _logger.Warning("Application update check failed: " + exception.Message);
                if (showResult) ShowError(exception.Message, "Application updates");
            }
            finally { SetUpdateBusy(false); }
        }

        private async Task DownloadAvailableUpdateAsync()
        {
            if (_updateBusy || _availableUpdate == null) return;
            SetUpdateBusy(true);
            _updateDownloadCancellation = new CancellationTokenSource();
            Find<TextBlock>("UpdateStatusText").Text = "Downloading and verifying version "
                + _availableUpdate.Version + "...";
            Find<TextBlock>("UpdateStatusText").Foreground = Brush("#5DD9FF");
            Find<ProgressBar>("UpdateProgressBar").Value = 0;
            IProgress<int> progress = new Progress<int>(delegate(int value)
            {
                Find<ProgressBar>("UpdateProgressBar").Value = value;
                Find<TextBlock>("UpdateStatusText").Text = "Downloading verified update... " + value + "%";
            });
            try
            {
                string path = await _appUpdates.DownloadPackageAsync(
                    _availableUpdate,
                    _paths.UpdateRoot,
                    progress,
                    _updateDownloadCancellation.Token
                );
                Find<TextBlock>("UpdateStatusText").Text = "Verified package ready: " + System.IO.Path.GetFileName(path);
                Find<TextBlock>("UpdateStatusText").Foreground = Brush("#39DBA0");
                ShowInfo(
                    "The verified update ZIP is ready. Nivan Shield did not install or replace any file automatically.\n\n"
                    + path,
                    "Update downloaded"
                );
            }
            catch (OperationCanceledException)
            {
                Find<TextBlock>("UpdateStatusText").Text = "Update download cancelled.";
            }
            catch (Exception exception)
            {
                Find<TextBlock>("UpdateStatusText").Text = "Update download failed: " + exception.Message;
                Find<TextBlock>("UpdateStatusText").Foreground = Brush("#FF667A");
                ShowError(exception.Message, "Download update");
            }
            finally
            {
                if (_updateDownloadCancellation != null)
                {
                    _updateDownloadCancellation.Dispose();
                    _updateDownloadCancellation = null;
                }
                SetUpdateBusy(false);
            }
        }

        private void SetUpdateBusy(bool busy)
        {
            _updateBusy = busy;
            Find<Button>("CheckUpdatesButton").IsEnabled = !busy;
            Find<Button>("DownloadUpdateButton").IsEnabled = !busy && _availableUpdate != null;
        }

        private async Task LaunchNekoRayAsync()
        {
            if (!SaveNekoControls()) return;
            try
            {
                await _manager.StartSshRoutingAsync();
                UpdateNekoVisual();
            }
            catch (Exception exception) { ShowError(exception.Message, "Integrated routing"); }
        }

        private void ApplyPortableEngineChoiceUi()
        {
            bool bundledCore = true;
            Find<CheckBox>("UseBundledCoreCheck").IsChecked = true;
            Find<CheckBox>("UseBundledCoreCheck").IsEnabled = false;
            TextBox corePath = Find<TextBox>("SingBoxPathInput");
            Button coreBrowse = Find<Button>("BrowseSingBoxButton");
            corePath.IsReadOnly = true;
            coreBrowse.IsEnabled = false;
            if (bundledCore && File.Exists(_paths.BundledNekoCorePath))
                corePath.Text = _paths.BundledNekoCorePath;
        }

        private void OnLogLineWritten(object sender, LogLineEventArgs eventArgs)
        {
            _window.Dispatcher.BeginInvoke(new Action(RefreshLogs), DispatcherPriority.Background);
        }

        private void RefreshLogs()
        {
            string content = _logger.ReadTail(500);
            TextBox full = Find<TextBox>("LogBox");
            full.Text = content;
            full.ScrollToEnd();
            string[] lines = content.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);
            int start = Math.Max(0, lines.Length - 8);
            Find<TextBox>("DashboardLogBox").Text = String.Join(Environment.NewLine, lines, start, lines.Length - start);
        }

        private void OnTimerTick(object sender, EventArgs eventArgs)
        {
            if (_connectedAt.HasValue && _manager.State == ConnectionState.Connected)
            {
                TimeSpan elapsed = DateTime.Now - _connectedAt.Value;
                Find<TextBlock>("UptimeText").Text = String.Format(
                    "{0:00}:{1:00}:{2:00}",
                    Math.Floor(elapsed.TotalHours),
                    elapsed.Minutes,
                    elapsed.Seconds
                );
            }
            else Find<TextBlock>("UptimeText").Text = "00:00:00";
            UpdateNekoVisual();
            ApplyLanguage();
        }

        private void ApplyLanguage()
        {
            _localization.Apply(_window, _settings.App.Language);
            ApplyLanguageLayout();
            UpdateShortcutHint(_settings.Shortcuts);
            UpdateTrayLanguage();
        }

        private void ApplyLanguageLayout()
        {
            bool persian = String.Equals(_settings.App.Language, "fa", StringComparison.OrdinalIgnoreCase);
            ColumnDefinition sidebarColumn = Find<ColumnDefinition>("SidebarColumn");
            ColumnDefinition contentColumn = Find<ColumnDefinition>("ContentColumn");
            Border sidebar = Find<Border>("SidebarPanel");
            Grid content = Find<Grid>("ContentPanel");
            sidebarColumn.Width = persian ? new GridLength(1, GridUnitType.Star) : new GridLength(214);
            contentColumn.Width = persian ? new GridLength(214) : new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(sidebar, persian ? 1 : 0);
            Grid.SetColumn(content, persian ? 0 : 1);
            sidebar.BorderThickness = persian ? new Thickness(1, 0, 0, 0) : new Thickness(0, 0, 1, 0);
            sidebar.FlowDirection = persian ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            foreach (string name in new string[]
            {
                "NavDashboard", "NavProfiles", "NavDns", "NavAbout", "NavConnection",
                "NavV2Ray", "NavPsiphon", "NavHealth", "NavNekoRay", "NavLogs"
            })
            {
                Find<Button>(name).HorizontalContentAlignment = persian
                    ? HorizontalAlignment.Right
                    : HorizontalAlignment.Left;
            }
        }

        private void CreateTrayIcon()
        {
            _trayIcon = new Forms.NotifyIcon();
            try
            {
                _trayIcon.Icon = Drawing.Icon.ExtractAssociatedIcon(
                    System.Reflection.Assembly.GetExecutingAssembly().Location
                ) ?? Drawing.SystemIcons.Shield;
            }
            catch { _trayIcon.Icon = Drawing.SystemIcons.Shield; }
            _trayIcon.Text = "Nivan Shield";
            _trayIcon.Visible = true;
            Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip();
            _trayOpenItem = menu.Items.Add(_localization.Translate("Open Nivan Shield", _settings.App.Language));
            _trayConnectItem = menu.Items.Add(_localization.Translate("Connect", _settings.App.Language));
            _trayDisconnectItem = menu.Items.Add(_localization.Translate("Disconnect", _settings.App.Language));
            menu.Items.Add(new Forms.ToolStripSeparator());
            _trayExitItem = menu.Items.Add(_localization.Translate("Exit", _settings.App.Language));
            _trayOpenItem.Click += delegate { _window.Dispatcher.BeginInvoke(new Action(ShowWindow)); };
            _trayConnectItem.Click += delegate
            {
                _window.Dispatcher.BeginInvoke(new Action(async delegate { await ConnectAsync(); }));
            };
            _trayDisconnectItem.Click += delegate
            {
                _window.Dispatcher.BeginInvoke(new Action(async delegate { await DisconnectAsync(); }));
            };
            _trayExitItem.Click += delegate
            {
                _window.Dispatcher.BeginInvoke(new Action(async delegate { await ExitAsync(); }));
            };
            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += delegate { _window.Dispatcher.BeginInvoke(new Action(ShowWindow)); };
        }

        private void UpdateTrayLanguage()
        {
            if (_trayOpenItem == null) return;
            _trayOpenItem.Text = _localization.Translate("Open Nivan Shield", _settings.App.Language);
            _trayConnectItem.Text = _localization.Translate("Connect", _settings.App.Language);
            _trayDisconnectItem.Text = _localization.Translate("Disconnect", _settings.App.Language);
            _trayExitItem.Text = _localization.Translate("Exit", _settings.App.Language);
        }

        private void ShowWindow()
        {
            _window.Show();
            _window.ShowInTaskbar = true;
            _window.WindowState = WindowState.Normal;
            _window.Activate();
        }

        private void HideToTray()
        {
            _window.Hide();
            _window.ShowInTaskbar = false;
            if (_trayIcon != null)
                _trayIcon.ShowBalloonTip(
                    1200,
                    _localization.Translate("Nivan Shield", _settings.App.Language),
                    _localization.Translate("The connection manager is still running.", _settings.App.Language),
                    Forms.ToolTipIcon.Info
                );
        }

        private async Task ExitAsync()
        {
            if (_settings.App.ConfirmExit && _manager.IsRunning
                && !Confirm("The connection is active. Disconnect and exit Nivan Shield?", "Exit Nivan Shield"))
                return;
            _allowExit = true;
            _manualDisconnectRequested = true;
            CancelPendingFailover();
            CancelQualityTest();
            if (_updateDownloadCancellation != null) _updateDownloadCancellation.Cancel();
            try { await _manager.DisconnectAsync(); }
            catch { }
            if (_trayIcon != null) _trayIcon.Visible = false;
            Application.Current.Shutdown();
        }

        private int ReadInteger(string controlName, string label, int minimum, int maximum)
        {
            int value;
            if (!Int32.TryParse(Find<TextBox>(controlName).Text.Trim(), out value) || value < minimum || value > maximum)
                throw new InvalidOperationException(label + " must be between " + minimum + " and " + maximum + ".");
            return value;
        }

        private static void ValidateHostAndUser(string host, string username)
        {
            if (!Regex.IsMatch(host ?? String.Empty, "^[a-zA-Z0-9][a-zA-Z0-9._:-]{0,252}$"))
                throw new InvalidOperationException("Enter a valid SSH host.");
            if (!Regex.IsMatch(username ?? String.Empty, "^[a-zA-Z0-9_][a-zA-Z0-9._-]{0,63}$"))
                throw new InvalidOperationException("Enter a valid SSH username.");
        }

        private static bool ContainsIgnoreCase(string value, string search)
        {
            return !String.IsNullOrWhiteSpace(value)
                && value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int ProfileAvailabilityRank(ConnectionProfile profile)
        {
            if (String.Equals(profile.LastTestStatus, "Online", StringComparison.OrdinalIgnoreCase)) return 0;
            if (String.Equals(profile.LastTestStatus, "Timeout", StringComparison.OrdinalIgnoreCase)) return 2;
            if (String.Equals(profile.LastTestStatus, "Offline", StringComparison.OrdinalIgnoreCase)) return 3;
            return 1;
        }

        private static long ProfileLatencyRank(ConnectionProfile profile)
        {
            return profile.LastLatencyMilliseconds >= 0 ? profile.LastLatencyMilliseconds : Int64.MaxValue;
        }

        private static void ResetProfileHealth(ConnectionProfile profile)
        {
            profile.LastLatencyMilliseconds = -1;
            profile.LastTestedUtc = null;
            profile.LastTestStatus = "Not tested";
        }

        private string ProfileTestDetail(ConnectionProfile profile)
        {
            if (!profile.LastTestedUtc.HasValue) return "This profile has not been tested yet.";
            DateTime tested = DateTime.SpecifyKind(profile.LastTestedUtc.Value, DateTimeKind.Utc).ToLocalTime();
            string result = profile.LastTestStatus;
            if (profile.LastLatencyMilliseconds >= 0) result += "  •  " + profile.LastLatencyMilliseconds + " ms";
            return result + "  •  Last checked " + tested.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private int FindNextSocksPort(int preferred)
        {
            HashSet<int> used = new HashSet<int>(_settings.Profiles.Items.Select(
                delegate(ConnectionProfile profile) { return profile.LocalSocksPort; }
            ));
            int start = Math.Max(1024, Math.Min(65535, preferred));
            for (int candidate = start; candidate <= 65535; candidate++)
            {
                if (!used.Contains(candidate)) return candidate;
            }
            for (int candidate = 1024; candidate < start; candidate++)
            {
                if (!used.Contains(candidate)) return candidate;
            }
            throw new InvalidOperationException("No free local SOCKS port is available.");
        }

        private string UniqueProfileName(string preferred)
        {
            string baseName = String.IsNullOrWhiteSpace(preferred) ? "SSH profile" : preferred;
            string candidate = baseName;
            int number = 2;
            while (_settings.Profiles.Items.Any(delegate(ConnectionProfile profile)
            {
                return String.Equals(profile.Name, candidate, StringComparison.OrdinalIgnoreCase);
            }))
            {
                candidate = baseName + " " + number;
                number++;
            }
            return candidate;
        }

        private static string SelectedText(ComboBox comboBox)
        {
            ComboBoxItem item = comboBox.SelectedItem as ComboBoxItem;
            return item == null || item.Content == null ? String.Empty : item.Content.ToString();
        }

        private static string SelectedTag(ComboBox comboBox)
        {
            ComboBoxItem item = comboBox.SelectedItem as ComboBoxItem;
            if (item == null) return String.Empty;
            return item.Tag == null
                ? (item.Content == null ? String.Empty : item.Content.ToString())
                : item.Tag.ToString();
        }

        private static void SelectComboTag(ComboBox comboBox, string tag)
        {
            foreach (object value in comboBox.Items)
            {
                ComboBoxItem item = value as ComboBoxItem;
                if (item != null && String.Equals(
                    item.Tag == null ? String.Empty : item.Tag.ToString(),
                    tag,
                    StringComparison.OrdinalIgnoreCase
                ))
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }
            if (comboBox.Items.Count > 0) comboBox.SelectedIndex = 0;
        }

        private static void SelectComboText(ComboBox comboBox, string text)
        {
            foreach (object value in comboBox.Items)
            {
                ComboBoxItem item = value as ComboBoxItem;
                if (item != null && String.Equals(
                    item.Content == null ? String.Empty : item.Content.ToString(),
                    text,
                    StringComparison.OrdinalIgnoreCase
                ))
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }
            if (comboBox.Items.Count > 0) comboBox.SelectedIndex = 0;
        }

        private static SolidColorBrush Brush(string color)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        }

        private void ShowInfo(string message) { ShowInfo(message, "Nivan Shield"); }

        private void ShowInfo(string message, string title)
        {
            MessageBox.Show(
                _window,
                _localization.Translate(message, _settings.App.Language),
                _localization.Translate(title, _settings.App.Language),
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }

        private void ShowError(string message) { ShowError(message, "Nivan Shield"); }

        private void ShowError(string message, string title)
        {
            MessageBox.Show(
                _window,
                _localization.Translate(message, _settings.App.Language),
                _localization.Translate(title, _settings.App.Language),
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }

        private bool Confirm(string message, string title)
        {
            return MessageBox.Show(
                _window,
                _localization.Translate(message, _settings.App.Language),
                _localization.Translate(title, _settings.App.Language),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            ) == MessageBoxResult.Yes;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            CancelPendingFailover();
            CancelQualityTest();
            if (_updateDownloadCancellation != null) _updateDownloadCancellation.Cancel();
            _timer.Stop();
            _manager.StateChanged -= OnConnectionStateChanged;
            _logger.LineWritten -= OnLogLineWritten;
            _manager.Dispose();
            _nekoRay.Dispose();
            _crashRecovery.EndSession();
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
        }
    }
}
