using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using Grpc.Net.Client;
using MouseKeyProxy.Common;
using MouseKeyProxy.Commands;
using MouseKeyProxy.Network;
using Wire = MouseKeyProxy.Network.V1;

namespace MouseKeyProxy.Agent;

/// <summary>
/// User-session tray agent. Menu items match docs/wireframes/01-tray-icon-menu.svg.
/// Uses custom logo from assets/logo.png.
/// </summary>
internal static class Program
{
    private static Win32InputInjector? _injector;
    private static Win32CursorClip? _clip;
    private static ToggleStateMachine? _state;
    private static Win32HotkeyMonitor? _hotkey;
    private static AgentControlPipeServer? _controlPipe;
    private static RemoteInputForwarder? _forwarder;
    private static Win32ClipboardListener? _clipboardListener;
    private static NotifyIcon? _tray;
    private static HotkeyMessageForm? _hotkeyWindow;
    private static Form? _dashboardForm;
    private static bool _exitRequested;
    private const string DefaultRemotePeer = "payton-desktop";
    private const string NotPairedText = "Not paired";
    private const string NotConnectedText = "Not connected to a remote";
    private static readonly JsonSerializerOptions PairingStateJsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly List<RemoteActionBinding> PairedRemoteActions = new();
    private static readonly List<RemoteActionBinding> ConnectedRemoteActions = new();
    private static readonly ToolTip DashboardToolTip = new();
    private static RemoteConnectionState _remoteState = RemoteConnectionState.NotPaired;
    private static string _activeRemotePeer = DefaultRemotePeer;
    private static string? _activeRemoteGrpcUrl;
    private static string? _lastPairingCode;
    private static string? _lastRemoteError;
    private static PeerCredential? _peerCredential;
    /// <summary>FR-MKP-027: optional clipboard-only peer (USB client Service), separate from Device input remote.</summary>
    private static string? _clipboardPeerUrl;
    private static PeerCredential? _clipboardCredential;
    private static System.Windows.Forms.Timer? _clipboardIntroTimer;
    private static Label? _pairingStatusValue;
    private static Label? _activePeerValue;
    private static Label? _remoteEndpointValue;
    private static Label? _recentErrorsValue;
    private static Button? _primaryRemoteButton;

    [STAThread]
    public static void Main()
    {
        ApplicationConfiguration.Initialize();

        _injector = new Win32InputInjector();
        _clip = new Win32CursorClip();
        _state = new ToggleStateMachine();
        _hotkey = new Win32HotkeyMonitor(HotkeyConfigStore.Load(HotkeyConfigStore.DefaultPath()));
        _controlPipe = AgentControlPipeServer.Start(new Win32DesktopController(), _injector, NotifyPairingState, GetAgentStatus, ExecuteEmergencyReleaseCommand, new Win32ScreenshotCapture());
        _forwarder = new RemoteInputForwarder(CreateRemoteChannel);
        LoadPersistedPairingState();
        try
        {
            _clipboardCredential = PeerCredentialStore.Load(ClipboardPeerCredentialPath());
            if (_clipboardCredential is not null)
            {
                var urlFile = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MouseKeyProxy",
                    "clipboard-peer-url.txt");
                if (File.Exists(urlFile))
                {
                    _clipboardPeerUrl = File.ReadAllText(urlFile).Trim();
                }
            }
        }
        catch
        {
            /* best effort */
        }

        _tray = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Visible = true,
            Text = "MouseKeyProxy"
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("MouseKeyProxy dashboard", null, (_, _) => ShowDashboardForm());
        AddConnectedRemoteMenuAction(menu, "Toggle Active - Desktop Control (Ctrl-Alt-F1)", (_, _) => DoRealToggle());
        menu.Items.Add("Pairing", null, (_, _) => ShowPairingForm());
        AddPairedRemoteMenuAction(menu, "Unpair remote…", (_, _) => UnpairRemoteInteractive());
        AddPairedRemoteMenuAction(menu, "Reconnect", (_, _) => TryReconnect());
        AddPairedRemoteMenuAction(menu, "Claim USB clipboard client…", (_, _) => ClaimUsbClipboardClientsInteractive());
        menu.Items.Add("Service", null, (_, _) => ShowServiceForm());
        menu.Items.Add("Discover folder shares...", null, (_, _) => DiscoverFolderShares());
        AddPairedRemoteMenuAction(menu, "Device configuration…", (_, _) => ShowDeviceManagementForm());
        // FR-MKP-018: share browse is the Share tab of Device configuration (no second incomplete surface).
        AddConnectedRemoteMenuAction(menu, "Clipboard", (_, _) => ShowClipboardForm());
        AddConnectedRemoteMenuAction(menu, "Inject Text to Remote...", (_, _) => ShowInjectForm());
        menu.Items.Add("Emergency release", null, (_, _) => EmergencyRelease());
        menu.Items.Add("Open logs", null, (_, _) => OpenLogs());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowDashboardForm();
        UpdateRemoteActionAvailability();

        _hotkey.ToggleRequested += (_, _) => DoRealToggle();
        _hotkey.EmergencyReleaseRequested += (_, _) => EmergencyRelease();
        _hotkey.StartMonitoring();

        // FR-MKP-004: forward local clipboard changes to the connected peer.
        _clipboardListener = new Win32ClipboardListener();
        _clipboardListener.ClipboardCaptured += OnLocalClipboardCaptured;
        _clipboardListener.StartListening();

        // FR-MKP-026: poll device for pending USB-client clipboard intros while paired.
        _clipboardIntroTimer = new System.Windows.Forms.Timer { Interval = 15_000 };
        _clipboardIntroTimer.Tick += (_, _) =>
        {
            if (_remoteState is RemoteConnectionState.Connected or RemoteConnectionState.NotConnected)
            {
                _ = System.Threading.Tasks.Task.Run(() => TryClaimUsbClipboardClients(quiet: true));
            }
        };
        _clipboardIntroTimer.Start();

        _hotkeyWindow = new HotkeyMessageForm(() => _hotkey.RaiseToggle("Ctrl-Alt-F1", false));
        _hotkey.RegisterForWindow(_hotkeyWindow.Handle, 0x0003, (uint)Keys.F1);

        Application.ApplicationExit += (_, _) => CleanupApplication();
        ShowDashboardForm();
        Application.Run();
    }

    private static Icon LoadTrayIcon()
    {
        var logoPath = Path.Combine(AppContext.BaseDirectory, "logo.png");
        if (!File.Exists(logoPath))
        {
            logoPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "assets", "logo.png"));
        }
        if (!File.Exists(logoPath))
        {
            logoPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "assets", "logo.png"));
        }
        if (File.Exists(logoPath))
        {
            using var bmp = new Bitmap(logoPath);
            return Icon.FromHandle(bmp.GetHicon());
        }
        var fallback = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(fallback))
        {
            g.Clear(Color.FromArgb(74, 144, 217));
        }
        return Icon.FromHandle(fallback.GetHicon());
    }

    private static void ShowInjectForm()
    {
        if (!EnsureConnectedRemoteAction("Inject Text to Remote"))
        {
            return;
        }

        using var form = new Form { Text = "Inject Text to Remote", Width = 360, Height = 240, StartPosition = FormStartPosition.CenterScreen };
        form.Controls.Add(new Label { Text = "Active peer:", Left = 16, Top = 16, AutoSize = true });
        form.Controls.Add(new ComboBox
        {
            Left = 104,
            Top = 12,
            Width = 232,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Items = { CurrentRemotePeer() },
            SelectedIndex = 0
        });
        form.Controls.Add(new Label { Text = "Text:", Left = 16, Top = 48, AutoSize = true });
        var text = new TextBox { Left = 16, Top = 72, Width = 320, Height = 80, Multiline = true };
        form.Controls.Add(text);
        var send = new Button { Text = "Send", Left = 200, Top = 168, Width = 64 };
        send.Click += (_, _) => { DoRealInject(text.Text); form.Close(); };
        form.Controls.Add(send);
        form.Controls.Add(new Button { Text = "Cancel", Left = 272, Top = 168, Width = 64, DialogResult = DialogResult.Cancel });
        form.ShowDialog();
    }

    private static void ShowDashboardForm()
    {
        if (_dashboardForm is null || _dashboardForm.IsDisposed)
        {
            _dashboardForm = CreateDashboardForm();
        }

        if (!_dashboardForm.Visible)
        {
            _dashboardForm.Show();
        }

        if (_dashboardForm.WindowState == FormWindowState.Minimized)
        {
            _dashboardForm.WindowState = FormWindowState.Normal;
        }

        _dashboardForm.Activate();
    }

    private static Form CreateDashboardForm()
    {
        var form = new Form
        {
            Text = "MouseKeyProxy dashboard",
            Icon = LoadTrayIcon(),
            StartPosition = FormStartPosition.CenterScreen,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(811, 433)
        };
        form.FormClosing += (_, args) =>
        {
            if (!_exitRequested && args.CloseReason == CloseReason.UserClosing)
            {
                args.Cancel = true;
                form.Hide();
            }
        };

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(24, 20, 24, 20),
            ColumnCount = 2,
            Margin = Padding.Empty,
            RowCount = 0
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        form.Controls.Add(layout);

        _pairingStatusValue = AddDashboardRow(layout, "Pairing", RemotePairingStatusText());
        _activePeerValue = AddDashboardRow(layout, "Active peer", RemoteActivePeerText());
        _remoteEndpointValue = AddDashboardRow(layout, "Remote endpoint", RemoteEndpointStatusText());
        AddDashboardRow(layout, "Clipboard", "Idle");
        _recentErrorsValue = AddDashboardRow(layout, "Recent errors", RecentErrorsText());

        var actions = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        _primaryRemoteButton = CreateDashboardButton("Pair");
        _primaryRemoteButton.Click += (_, _) =>
        {
            if (_remoteState == RemoteConnectionState.NotPaired)
            {
                ShowPairingForm();
            }
            else
            {
                TryReconnect();
            }
        };
        actions.Controls.Add(_primaryRemoteButton);
        var release = CreateDashboardButton("Emergency release");
        release.Click += (_, _) => EmergencyRelease();
        actions.Controls.Add(release);
        var logs = CreateDashboardButton("Open logs");
        logs.Click += (_, _) => OpenLogs();
        actions.Controls.Add(logs);

        var actionRow = layout.RowStyles.Count;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            Text = "Controls",
            Font = CreateBoldMessageFont(),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 28, 0)
        }, 0, actionRow);
        layout.Controls.Add(actions, 1, actionRow);
        UpdateRemoteActionAvailability();

        return form;
    }

    private static Label AddDashboardRow(TableLayoutPanel layout, string label, string value)
    {
        var row = layout.RowStyles.Count;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            Text = label,
            Font = CreateBoldMessageFont(),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 4, 28, 0)
        }, 0, row);
        var valueLabel = new Label
        {
            Text = value,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = true,
            AutoEllipsis = true,
            Margin = new Padding(0, 4, 0, 0)
        };
        layout.Controls.Add(valueLabel, 1, row);
        return valueLabel;
    }

    private static Button CreateDashboardButton(string text)
    {
        return new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            TextAlign = ContentAlignment.MiddleCenter,
            MinimumSize = new Size(0, 48),
            Padding = new Padding(18, 8, 18, 8),
            Margin = new Padding(0, 0, 14, 0),
            UseVisualStyleBackColor = true
        };
    }

    private static Font CreateBoldMessageFont()
    {
        return new Font(SystemFonts.MessageBoxFont ?? Control.DefaultFont, FontStyle.Bold);
    }

    private static void AddPairedRemoteMenuAction(ContextMenuStrip menu, string text, EventHandler onClick)
    {
        var item = menu.Items.Add(text, null, onClick);
        PairedRemoteActions.Add(RemoteActionBinding.ForMenuItem(item, text));
    }

    private static void AddConnectedRemoteMenuAction(ContextMenuStrip menu, string text, EventHandler onClick)
    {
        var item = menu.Items.Add(text, null, onClick);
        ConnectedRemoteActions.Add(RemoteActionBinding.ForMenuItem(item, text));
    }

    /// <summary>
    /// FR-MKP-013 / FR-MKP-014 / FR-MKP-018: complete paired-device configuration surface
    /// (functions, media, share/SMB, events, pairing assist).
    /// </summary>
    /// <param name="initialTab">0=Functions, 1=Media, 2=Share, 3=Events, 4=Pairing.</param>
    private static void ShowDeviceManagementForm(int initialTab = 0)
    {
        if (!EnsurePairedRemoteAction("Device configuration"))
        {
            return;
        }

        try
        {
            var url = ResolveRemoteGrpcUrl();
            using var form = new DeviceManagementForm(
                () => CreateRemoteChannel(url),
                Environment.MachineName.ToLowerInvariant(),
                remoteHostHint: url,
                initialTab: initialTab,
                onUnpairLocal: UnpairLocal);
            form.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Device configuration", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>FR-MKP-014: listen for LAN beacons advertising folder shares (any MKP peer, incl. Pi).</summary>
    private static void DiscoverFolderShares()
    {
        try
        {
            var beacons = DiscoveryFinder
                .ListenAsync(TimeSpan.FromSeconds(5), filter: DiscoveryFinder.DiscoveryFilter.FolderShareAvailable)
                .GetAwaiter().GetResult();
            if (beacons.Count == 0)
            {
                MessageBox.Show(
                    "No folder shares found on the LAN.\n\nOn the device set MKP_FOLDER_SHARE=1 and ensure UDP 50052 inbound is allowed on this PC.",
                    "MouseKeyProxy",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var lines = string.Join(
                Environment.NewLine,
                beacons.Select(b =>
                    $"{b.PeerId}  share={b.FolderShareName}  https://{b.Host}:{b.GrpcPort}  pairing={b.PairingAvailable}"));
            MessageBox.Show(lines, "Folder shares discovered", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Discover folder shares", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static void UpdateRemoteActionAvailability()
    {
        ApplyRemoteActionAvailability(PairedRemoteActions, _remoteState != RemoteConnectionState.NotPaired, NotPairedText);
        ApplyRemoteActionAvailability(ConnectedRemoteActions, _remoteState == RemoteConnectionState.Connected, RemoteActionBlockReason());
        UpdatePrimaryRemoteButton();
        UpdateDashboardStatusLabels();
    }

    private static void UpdatePrimaryRemoteButton()
    {
        if (_primaryRemoteButton is null || _primaryRemoteButton.IsDisposed)
        {
            return;
        }

        if (_remoteState == RemoteConnectionState.NotPaired)
        {
            _primaryRemoteButton.Text = "Pair";
            _primaryRemoteButton.Enabled = true;
            DashboardToolTip.SetToolTip(_primaryRemoteButton, $"Pair with {CurrentRemotePeer()}");
            return;
        }

        _primaryRemoteButton.Text = "Reconnect";
        _primaryRemoteButton.Enabled = true;
        DashboardToolTip.SetToolTip(_primaryRemoteButton, $"Reconnect to {CurrentRemotePeer()}");
    }

    private static void ApplyRemoteActionAvailability(
        IEnumerable<RemoteActionBinding> bindings,
        bool enabled,
        string reason)
    {
        foreach (var binding in bindings)
        {
            var text = enabled ? binding.EnabledText : $"{binding.EnabledText} ({reason})";
            if (binding.MenuItem is not null && !binding.MenuItem.IsDisposed)
            {
                binding.MenuItem.Text = text;
                binding.MenuItem.Enabled = enabled;
                binding.MenuItem.ToolTipText = enabled ? string.Empty : reason;
            }

            if (binding.Button is not null && !binding.Button.IsDisposed)
            {
                binding.Button.Text = text;
                binding.Button.Enabled = enabled;
                DashboardToolTip.SetToolTip(binding.Button, enabled ? string.Empty : reason);
            }
        }
    }

    private static void UpdateDashboardStatusLabels()
    {
        if (_pairingStatusValue is not null && !_pairingStatusValue.IsDisposed)
        {
            _pairingStatusValue.Text = RemotePairingStatusText();
        }

        if (_activePeerValue is not null && !_activePeerValue.IsDisposed)
        {
            _activePeerValue.Text = RemoteActivePeerText();
        }

        if (_remoteEndpointValue is not null && !_remoteEndpointValue.IsDisposed)
        {
            _remoteEndpointValue.Text = RemoteEndpointStatusText();
        }

        if (_recentErrorsValue is not null && !_recentErrorsValue.IsDisposed)
        {
            _recentErrorsValue.Text = RecentErrorsText();
        }
    }

    // TR-MKP-UI-001: surface the last remote error (audit: _lastRemoteError was tracked but never shown).
    private static string RecentErrorsText()
    {
        if (string.IsNullOrWhiteSpace(_lastRemoteError))
        {
            return "None recorded this session";
        }

        return _lastRemoteError.Length > 80 ? _lastRemoteError[..80] + "..." : _lastRemoteError;
    }

    private static string RemoteActionBlockReason()
    {
        return _remoteState switch
        {
            RemoteConnectionState.NotConnected => NotConnectedText,
            _ => NotPairedText
        };
    }

    private static string RemotePairingStatusText()
    {
        return _remoteState switch
        {
            RemoteConnectionState.Connected => "Paired and connected",
            RemoteConnectionState.NotConnected when !string.IsNullOrWhiteSpace(_lastRemoteError) => $"{NotConnectedText} (see logs)",
            RemoteConnectionState.NotConnected => NotConnectedText,
            _ => "Ready for local code entry"
        };
    }

    private static string RemoteActivePeerText()
    {
        return _remoteState == RemoteConnectionState.Connected
            ? CurrentRemotePeer()
            : RemoteActionBlockReason();
    }

    private static string RemoteEndpointStatusText()
    {
        if (_remoteState == RemoteConnectionState.NotPaired)
        {
            return "None";
        }

        var remoteUrl = ResolveRemoteGrpcUrl();
        return _remoteState == RemoteConnectionState.Connected
            ? remoteUrl
            : $"{remoteUrl} ({RemoteActionBlockReason()})";
    }

    private static bool EnsurePairedRemoteAction(string action)
    {
        if (_remoteState != RemoteConnectionState.NotPaired)
        {
            return true;
        }

        ShowBlockedRemoteAction(action, NotPairedText);
        return false;
    }

    private static bool EnsureConnectedRemoteAction(string action)
    {
        if (_remoteState == RemoteConnectionState.Connected)
        {
            return true;
        }

        ShowBlockedRemoteAction(action, RemoteActionBlockReason());
        return false;
    }

    private static void ShowBlockedRemoteAction(string action, string reason)
    {
        UpdateRemoteActionAvailability();
        MessageBox.Show(
            $"{action} requires a paired and connected remote. {reason}.",
            action,
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    private static bool TryPairRemote(string pairingCode, out string message)
    {
        var remoteUrl = ResolveRemoteGrpcUrl();
        try
        {
            // TR-MKP-SEC-001: real pairing - exchange the one-time code for a service-signed client
            // certificate over mTLS, then persist the credential for authenticated forwarding.
            var peerId = Environment.MachineName.ToLowerInvariant();
            var credential = PairingClient.PairAsync(remoteUrl, peerId, pairingCode).GetAwaiter().GetResult();

            _peerCredential = credential;
            PeerCredentialStore.Save(PeerCredentialStore.DefaultPath(), credential);

            ApplyPairingState(ResolveRemotePeerName(remoteUrl), remoteUrl, pairingCode, connected: true);
            message = $"Paired and connected to {CurrentRemotePeer()}.";
            return true;
        }
        catch (PairingException ex)
        {
            _remoteState = RemoteConnectionState.NotPaired;
            _lastPairingCode = null;
            _lastRemoteError = ex.Error;
            message = $"Not paired: {ex.Error}";
            UpdateRemoteActionAvailability();
            return false;
        }
        catch (Exception ex)
        {
            _remoteState = RemoteConnectionState.NotConnected;
            _activeRemoteGrpcUrl = remoteUrl;
            _activeRemotePeer = ResolveRemotePeerName(remoteUrl);
            if (!string.IsNullOrWhiteSpace(pairingCode))
            {
                _lastPairingCode = pairingCode;
            }

            _lastRemoteError = ex.Message;
            message = $"{NotConnectedText}: {ex.Message}";
            PersistPairingState(connected: false);
            UpdateRemoteActionAvailability();
            return false;
        }
    }

    /// <summary>
    /// TR-MKP-SEC-001: builds a mutually-authenticated channel to the remote service using the
    /// persisted peer credential, loading it from disk on first use. Returns null when unpaired.
    /// </summary>
    private static GrpcChannel? CreateRemoteChannel(string remoteUrl)
    {
        _peerCredential ??= PeerCredentialStore.Load(PeerCredentialStore.DefaultPath());
        return _peerCredential is null ? null : PairingClient.CreateAuthenticatedChannel(remoteUrl, _peerCredential);
    }

    /// <summary>
    /// FR-MKP-004 / FR-MKP-027: forward clipboard to the ClipboardClient peer when registered;
    /// never send clipboard to the Device appliance when a clipboard peer exists.
    /// Keyboard/mouse continue to use the device remote only.
    /// </summary>
    private static void OnLocalClipboardCaptured(object? sender, ClipboardEventArgs e)
    {
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                // Prefer dedicated clipboard peer (USB client Service).
                if (!string.IsNullOrWhiteSpace(_clipboardPeerUrl) && _clipboardCredential is not null)
                {
                    PeerTrafficPolicy.EnsureAllowed(
                        PeerEffectRole.ClipboardClient,
                        PeerTrafficPolicy.EffectKind.Clipboard,
                        _clipboardPeerUrl);
                    using var clipChannel = PairingClient.CreateAuthenticatedChannel(
                        _clipboardPeerUrl, _clipboardCredential);
                    var clipClient = new Wire.MouseKeyProxy.MouseKeyProxyClient(clipChannel);
                    using var clipTransport = new BidiSessionTransport(clipClient);
                    await clipTransport.SendClipboardAsync(e.Entry);
                    return;
                }

                // No clipboard peer: do not push clipboard to the HID device appliance.
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MouseKeyProxy clipboard forward failed: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// FR-MKP-026: host claims pending USB-client clipboard intros from the paired device.
    /// </summary>
    private static void ClaimUsbClipboardClientsInteractive()
    {
        var msg = TryClaimUsbClipboardClients(quiet: false);
        MessageBox.Show(
            msg,
            "Claim USB clipboard client",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private static string TryClaimUsbClipboardClients(bool quiet)
    {
        try
        {
            if (_remoteState == RemoteConnectionState.NotPaired)
            {
                return quiet ? string.Empty : "Not paired to a device.";
            }

            var deviceUrl = ResolveRemoteGrpcUrl();
            using var channel = CreateRemoteChannel(deviceUrl);
            if (channel is null)
            {
                return quiet ? string.Empty : "No device channel / credential.";
            }

            var client = new Wire.MouseKeyProxy.MouseKeyProxyClient(channel);
            var pending = client.GetPendingClientIntros(new Wire.GetPendingClientIntrosRequest
            {
                ProtocolVersion = "v1",
                CorrelationId = Guid.NewGuid().ToString("n"),
            });
            if (!pending.Ok)
            {
                return quiet ? string.Empty : $"GetPendingClientIntros failed: {pending.Err} {pending.Msg}";
            }

            if (pending.Intros.Count == 0)
            {
                return quiet ? string.Empty : "No pending USB clipboard clients.";
            }

            var claimedAny = 0;
            foreach (var intro in pending.Intros)
            {
                var claimed = client.ClaimClientIntro(new Wire.ClaimClientIntroRequest
                {
                    ProtocolVersion = "v1",
                    ClientPeerId = intro.ClientPeerId,
                    CorrelationId = Guid.NewGuid().ToString("n"),
                });
                if (!claimed.Ok || claimed.Intro is null)
                {
                    continue;
                }

                var i = claimed.Intro;
                var cred = PairingClient.PairAsync(
                    i.ClipboardEndpoint,
                    Environment.MachineName.ToLowerInvariant() + "-host",
                    i.ClipboardIntroCode).GetAwaiter().GetResult();

                _clipboardCredential?.ClientCertificate.Dispose();
                _clipboardCredential?.CaCertificate.Dispose();
                _clipboardCredential = cred;
                _clipboardPeerUrl = i.ClipboardEndpoint;
                PeerCredentialStore.Save(ClipboardPeerCredentialPath(), cred);
                try
                {
                    var urlFile = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "MouseKeyProxy",
                        "clipboard-peer-url.txt");
                    File.WriteAllText(urlFile, i.ClipboardEndpoint);
                }
                catch
                {
                    /* best effort */
                }

                claimedAny++;
            }

            return claimedAny > 0
                ? $"Claimed {claimedAny} clipboard client(s). Input remains on device; clipboard → {_clipboardPeerUrl}."
                : (quiet ? string.Empty : "Pending intros could not be claimed/paired.");
        }
        catch (Exception ex)
        {
            return quiet ? string.Empty : $"Claim failed: {ex.Message}";
        }
    }

    private static string ClipboardPeerCredentialPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MouseKeyProxy",
        "clipboard-peer-credential.bin");

    private static AgentControlResponse NotifyPairingState(AgentControlRequest request)
    {
        if (_hotkeyWindow is { IsHandleCreated: true } window && window.InvokeRequired)
        {
            return (AgentControlResponse)window.Invoke(new Func<AgentControlResponse>(() => NotifyPairingState(request)));
        }

        if (string.IsNullOrWhiteSpace(request.RemoteGrpcUrl))
        {
            return AgentControlResponse.Failure("PAIRING_STATE_BAD_REQUEST", "RemoteGrpcUrl is required.");
        }

        var peer = string.IsNullOrWhiteSpace(request.RemotePeer)
            ? ResolveRemotePeerName(request.RemoteGrpcUrl)
            : request.RemotePeer.Trim();
        ApplyPairingState(peer, request.RemoteGrpcUrl.Trim(), request.PairingCode, connected: true);
        return AgentControlResponse.Success($"paired and connected to {CurrentRemotePeer()}");
    }

    private static AgentControlResponse GetAgentStatus()
    {
        if (_hotkeyWindow is { IsHandleCreated: true } window && window.InvokeRequired)
        {
            return (AgentControlResponse)window.Invoke(new Func<AgentControlResponse>(GetAgentStatus));
        }

        return new AgentControlResponse
        {
            Ok = true,
            ErrorCode = "0",
            Message = "agent status",
            RemotePeer = CurrentRemotePeer(),
            RemoteGrpcUrl = RemoteEndpointStatusText(),
            RemoteState = _remoteState.ToString(),
            ForwardingActive = _forwarder?.IsActive ?? false
        };
    }

    private static void ApplyPairingState(string remotePeer, string remoteGrpcUrl, string pairingCode, bool connected)
    {
        _activeRemotePeer = string.IsNullOrWhiteSpace(remotePeer) ? ResolveRemotePeerName(remoteGrpcUrl) : remotePeer;
        _activeRemoteGrpcUrl = string.IsNullOrWhiteSpace(remoteGrpcUrl) ? null : remoteGrpcUrl;
        _remoteState = connected ? RemoteConnectionState.Connected : RemoteConnectionState.NotConnected;
        _lastPairingCode = pairingCode;
        _lastRemoteError = null;
        PersistPairingState(connected);
        UpdateRemoteActionAvailability();
    }

    private static string CurrentRemotePeer()
    {
        return string.IsNullOrWhiteSpace(_activeRemotePeer) ? DefaultRemotePeer : _activeRemotePeer;
    }

    private static string ResolveRemotePeerName(string remoteGrpcUrl)
    {
        return Uri.TryCreate(remoteGrpcUrl, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host)
            ? uri.Host.ToLowerInvariant()
            : DefaultRemotePeer;
    }

    private static string PairingStatePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MouseKeyProxy",
            "agent-pairing.json");
    }

    private static void LoadPersistedPairingState()
    {
        try
        {
            var path = PairingStatePath();
            if (!File.Exists(path))
            {
                return;
            }

            var state = JsonSerializer.Deserialize<PersistedPairingState>(
                File.ReadAllText(path),
                PairingStateJsonOptions);
            if (state is null || string.IsNullOrWhiteSpace(state.RemoteGrpcUrl))
            {
                return;
            }

            _activeRemotePeer = string.IsNullOrWhiteSpace(state.RemotePeer)
                ? ResolveRemotePeerName(state.RemoteGrpcUrl)
                : state.RemotePeer;
            _activeRemoteGrpcUrl = state.RemoteGrpcUrl;
            _lastPairingCode = state.PairingCode;
            _lastRemoteError = state.Connected ? null : NotConnectedText;
            _remoteState = state.Connected ? RemoteConnectionState.Connected : RemoteConnectionState.NotConnected;
            _peerCredential = PeerCredentialStore.Load(PeerCredentialStore.DefaultPath());
        }
        catch
        {
            _remoteState = RemoteConnectionState.NotPaired;
            _lastPairingCode = null;
            _lastRemoteError = null;
        }
    }

    private static void PersistPairingState(bool connected)
    {
        try
        {
            var path = PairingStatePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var state = new PersistedPairingState
            {
                RemotePeer = CurrentRemotePeer(),
                RemoteGrpcUrl = ResolveRemoteGrpcUrl(),
                PairingCode = _lastPairingCode ?? string.Empty,
                Connected = connected,
                SavedAtUtc = DateTimeOffset.UtcNow
            };
            File.WriteAllText(path, JsonSerializer.Serialize(state, PairingStateJsonOptions));
        }
        catch (Exception ex)
        {
            _lastRemoteError = ex.Message;
        }
    }

    private static void ClearPersistedPairingState()
    {
        try
        {
            var path = PairingStatePath();
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            PeerCredentialStore.Delete(PeerCredentialStore.DefaultPath());
        }
        catch (Exception ex)
        {
            _lastRemoteError = ex.Message;
        }
    }

    /// <summary>
    /// Clears local pairing (agent-pairing.json + peer-credential.bin), stops forwarding,
    /// and best-effort calls device <c>Unpair</c> so the Pi re-opens ToFU when no peers remain.
    /// </summary>
    private static void UnpairRemoteInteractive()
    {
        if (_remoteState == RemoteConnectionState.NotPaired)
        {
            MessageBox.Show("Already unpaired.", "Unpair", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var peer = CurrentRemotePeer();
        var url = ResolveRemoteGrpcUrl();
        var confirm = MessageBox.Show(
            $"Unpair from {peer}?\n\nEndpoint: {url}\n\n" +
            "This clears local credentials on this PC and asks the remote to revoke this peer " +
            "(if the device implements Unpair). You can then pair to another device (e.g. the Pi).",
            "Unpair remote",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes)
        {
            return;
        }

        var remoteMsg = TryUnpairOnRemote();
        UnpairLocal();
        MessageBox.Show(
            $"Local pairing cleared.\n{remoteMsg}",
            "Unpair",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private static void UnpairLocal()
    {
        try
        {
            _forwarder?.Stop();
        }
        catch
        {
            /* best effort */
        }

        _peerCredential?.ClientCertificate.Dispose();
        _peerCredential?.CaCertificate.Dispose();
        _peerCredential = null;
        _activeRemotePeer = DefaultRemotePeer;
        _activeRemoteGrpcUrl = null;
        _lastPairingCode = null;
        _lastRemoteError = null;
        _remoteState = RemoteConnectionState.NotPaired;
        ClearPersistedPairingState();
        UpdateRemoteActionAvailability();
    }

    private static string TryUnpairOnRemote()
    {
        try
        {
            var url = ResolveRemoteGrpcUrl();
            using var channel = CreateRemoteChannel(url);
            if (channel is null)
            {
                return "Remote Unpair skipped (no channel / credential).";
            }

            var client = new Wire.MouseKeyProxy.MouseKeyProxyClient(channel);
            var response = client.Unpair(new Wire.UnpairRequest
            {
                ProtocolVersion = "v1",
                PeerId = Environment.MachineName.ToLowerInvariant(),
                CorrelationId = Guid.NewGuid().ToString("n"),
                ClearAll = false,
            });
            return response.Ok
                ? $"Remote: {response.Msg}"
                : $"Remote Unpair failed: {response.Err} {response.Msg}";
        }
        catch (Exception ex)
        {
            // Older remotes without Unpair RPC return Unimplemented.
            return $"Remote Unpair not completed ({ex.Message}). Local state still cleared.";
        }
    }

    private static void MarkRemoteDisconnected(Exception ex)
    {
        _remoteState = RemoteConnectionState.NotConnected;
        _lastRemoteError = ex.Message;
        PersistPairingState(connected: false);
        UpdateRemoteActionAvailability();
    }

    private static void ShowPairingForm()
    {
        using var form = new Form
        {
            Text = "Pairing",
            StartPosition = FormStartPosition.CenterScreen,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(16)
        };
        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 0,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        form.Controls.Add(layout);

        AddDashboardRow(layout, "Remote", CurrentRemotePeer());
        var status = AddDashboardRow(layout, "Status", RemotePairingStatusText());

        var endpointRow = layout.RowStyles.Count;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            Text = "Endpoint",
            Font = CreateBoldMessageFont(),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 8, 28, 0)
        }, 0, endpointRow);
        var endpoint = new TextBox
        {
            Width = 320,
            Margin = new Padding(0, 8, 0, 0),
            Text = ResolveRemoteGrpcUrl(),
            PlaceholderText = "https://192.168.1.200:50051",
        };
        layout.Controls.Add(endpoint, 1, endpointRow);

        var row = layout.RowStyles.Count;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            Text = "Code",
            Font = CreateBoldMessageFont(),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 8, 28, 0)
        }, 0, row);
        var code = new TextBox
        {
            Width = 240,
            Margin = new Padding(0, 8, 0, 0),
            Text = _lastPairingCode ?? string.Empty,
            PlaceholderText = "(empty = ToFU first pair)",
        };
        layout.Controls.Add(code, 1, row);

        var actions = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        var pair = CreateDashboardButton("Pair");
        pair.Click += async (_, _) =>
        {
            // Empty code is valid for ToFU (first pair on an unpaired device with MKP_TOFU=1).
            // After the first peer is registered, the service rejects codeless Pair with INVALID_OR_EXPIRED_CODE.
            var remoteUrl = endpoint.Text.Trim();
            if (string.IsNullOrWhiteSpace(remoteUrl))
            {
                status.Text = "Enter the remote endpoint (e.g. https://192.168.1.200:50051).";
                return;
            }

            if (!remoteUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                !remoteUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                remoteUrl = "https://" + remoteUrl.TrimStart('/');
                endpoint.Text = remoteUrl;
            }

            _activeRemoteGrpcUrl = remoteUrl;
            PersistRemoteEndpoint(remoteUrl);

            var pairingCode = code.Text.Trim();
            status.Text = string.IsNullOrWhiteSpace(pairingCode)
                ? "Pairing without a code (ToFU)…"
                : "Pairing…";
            pair.Enabled = false;
            try
            {
                // Off the UI thread so PairAsync cannot deadlock the WinForms sync context.
                var result = await Task.Run(() =>
                {
                    var ok = TryPairRemote(pairingCode, out var msg);
                    return (ok, msg);
                }).ConfigureAwait(true);
                status.Text = result.msg;
            }
            finally
            {
                pair.Enabled = true;
            }
        };
        actions.Controls.Add(pair);
        var unpair = CreateDashboardButton("Unpair");
        unpair.Click += (_, _) =>
        {
            if (_remoteState == RemoteConnectionState.NotPaired)
            {
                status.Text = "Already unpaired.";
                return;
            }

            var remoteMsg = TryUnpairOnRemote();
            UnpairLocal();
            status.Text = $"Unpaired. {remoteMsg}";
            code.Text = string.Empty;
        };
        actions.Controls.Add(unpair);
        var close = CreateDashboardButton("Close");
        close.Click += (_, _) => form.Close();
        actions.Controls.Add(close);

        var actionRow = layout.RowStyles.Count;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { AutoSize = true }, 0, actionRow);
        layout.Controls.Add(actions, 1, actionRow);
        form.ShowDialog();
    }

    private static void ShowServiceForm()
    {
        MessageBox.Show(
            $"Service endpoint: {Environment.GetEnvironmentVariable("MKP_GRPC") ?? "http://localhost:50051"}",
            "Service",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private static void ShowClipboardForm()
    {
        if (!EnsureConnectedRemoteAction("Clipboard"))
        {
            return;
        }

        MessageBox.Show(
            "Clipboard sync is idle.",
            "Clipboard",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private static void TryReconnect()
    {
        if (!EnsurePairedRemoteAction("Reconnect"))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_lastPairingCode))
        {
            _remoteState = RemoteConnectionState.NotPaired;
            UpdateRemoteActionAvailability();
            ShowBlockedRemoteAction("Reconnect", NotPairedText);
            return;
        }

        var ok = TryPairRemote(_lastPairingCode, out var message);
        MessageBox.Show(
            message,
            "Reconnect",
            MessageBoxButtons.OK,
            ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private static void EmergencyRelease()
    {
        PerformEmergencyRelease(showUi: true, notifyPeer: true, source: Environment.MachineName);
    }

    private static AgentControlResponse ExecuteEmergencyReleaseCommand(AgentControlRequest request)
    {
        if (_hotkeyWindow is { IsHandleCreated: true } window && window.InvokeRequired)
        {
            return (AgentControlResponse)window.Invoke(new Func<AgentControlResponse>(() => ExecuteEmergencyReleaseCommand(request)));
        }

        return PerformEmergencyRelease(showUi: false, notifyPeer: request.NotifyPeer, source: request.RemotePeer);
    }

    private static AgentControlResponse PerformEmergencyRelease(bool showUi, bool notifyPeer, string? source)
    {
        var failures = new List<string>();
        ClearLocalModifiers();
        var peerClear = notifyPeer ? TryRequestPeerClearModifiers() : null;
        var peerRelease = notifyPeer ? TryRequestPeerEmergencyRelease() : null;

        try
        {
            _forwarder?.Stop();
        }
        catch (Exception ex)
        {
            failures.Add($"forwarder stop failed: {ex.Message}");
        }

        try
        {
            _clip?.Release();
        }
        catch (Exception ex)
        {
            failures.Add($"cursor release failed: {ex.Message}");
        }

        _state?.Reset();
        UpdateRemoteActionAvailability();

        if (peerClear is { Ok: false })
        {
            failures.Add($"peer modifier clear failed: {peerClear.Value.ErrorCode}: {peerClear.Value.Message}");
        }

        if (peerRelease is { Ok: false })
        {
            failures.Add($"peer release failed: {peerRelease.Value.ErrorCode}: {peerRelease.Value.Message}");
        }

        var ok = failures.Count == 0;
        var sourceSuffix = string.IsNullOrWhiteSpace(source) ? string.Empty : $" requested by {source.Trim()}";
        var message = ok
            ? $"Emergency release completed{sourceSuffix}. Local forwarding is stopped and keyboard/mouse are restored."
            : $"Emergency release completed locally{sourceSuffix}, but {string.Join("; ", failures)}.";

        if (showUi)
        {
            MessageBox.Show(
                message,
                "Emergency release",
                MessageBoxButtons.OK,
                ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        return ok
            ? AgentControlResponse.Success(message)
            : AgentControlResponse.Failure("EMERGENCY_RELEASE_PARTIAL", message);
    }

    private static RemoteControlResult? TryRequestPeerClearModifiers()
    {
        if (_remoteState == RemoteConnectionState.NotPaired || string.IsNullOrWhiteSpace(_activeRemoteGrpcUrl))
        {
            return null;
        }

        try
        {
            // TR-MKP-AGENTIPC-001: dispatch through the shared command implementation (same code the REPL uses).
            using var channel = CreateRemoteChannel(_activeRemoteGrpcUrl);
            var commands = new RemoteServiceCommands(() => channel is null ? null : new Wire.MouseKeyProxy.MouseKeyProxyClient(channel));
            return commands.ClearModifiersAsync(Environment.MachineName.ToLowerInvariant(), Guid.NewGuid().ToString("N"))
                .GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            return RemoteControlResult.Failure("CLEAR_MODIFIERS_RPC_FAILED", ex.Message);
        }
    }

    private static AgentControlResponse ClearLocalModifiers()
    {
        if (_injector is null)
        {
            return AgentControlResponse.Failure("LOCAL_INJECTOR_UNAVAILABLE", "Local input injector is not configured.");
        }

        var releases = ModifierReleasePolicy.CreateKeyUpEvents();
        return _injector.TryInjectBatch(releases, out var error)
            ? AgentControlResponse.Success($"cleared {releases.Count} local modifiers")
            : AgentControlResponse.Failure("LOCAL_CLEAR_MODIFIERS_FAILED", error ?? "Local modifier cleanup failed.");
    }

    private static RemoteControlResult? TryRequestPeerEmergencyRelease()
    {
        if (_remoteState == RemoteConnectionState.NotPaired || string.IsNullOrWhiteSpace(_activeRemoteGrpcUrl))
        {
            return null;
        }

        try
        {
            // TR-MKP-AGENTIPC-001: dispatch through the shared command implementation (same code the REPL uses).
            using var channel = CreateRemoteChannel(_activeRemoteGrpcUrl);
            var commands = new RemoteServiceCommands(() => channel is null ? null : new Wire.MouseKeyProxy.MouseKeyProxyClient(channel));
            return commands.EmergencyReleaseAsync(Environment.MachineName.ToLowerInvariant(), Guid.NewGuid().ToString("N"))
                .GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            return RemoteControlResult.Failure("EMERGENCY_RELEASE_RPC_FAILED", ex.Message);
        }
    }

    private static void OpenLogs()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "eventvwr.msc",
            Arguments = "/c:MouseKeyProxy",
            UseShellExecute = true
        });
    }

    private static void ExitApplication()
    {
        _exitRequested = true;
        Application.Exit();
    }

    private static void CleanupApplication()
    {
        if (_tray is not null)
        {
            _tray.Visible = false;
        }

        _forwarder?.Stop();
        _clip?.Release();
        ClearLocalModifiers();
        _clipboardListener?.Dispose();
        _tray?.Dispose();
        _hotkeyWindow?.Dispose();
        _dashboardForm?.Dispose();
        _forwarder?.Dispose();
        _controlPipe?.Dispose();
    }

    private static void ApplyClipForActive(bool active)
    {
        if (active)
        {
            _clip?.ClipToPoint(100, 100);
        }
        else
        {
            _clip?.Release();
        }
    }

    // async void: this is a WinForms event handler (menu click + hotkey). Awaiting keeps the UI
    // message pump live during the toggle RPC instead of blocking it with .GetResult().
    private static async void DoRealToggle()
    {
        if (!EnsureConnectedRemoteAction("Toggle Active - Desktop Control"))
        {
            return;
        }

        try
        {
            ClearLocalModifiers();
            TryRequestPeerClearModifiers();
            var remoteUrl = ResolveRemoteGrpcUrl();
            var activePeer = remoteUrl;
            bool active = await InputCommandHandler.ToggleAsync(_state!, null, activePeer);
            if (active)
            {
                // Engage the cursor clip so the local pointer is confined while input forwards to the
                // remote (previously ApplyClipForActive(true) was never called - the clip never engaged).
                ApplyClipForActive(true);
                _forwarder!.Start(remoteUrl);
            }
            else
            {
                _forwarder?.Stop();
                ApplyClipForActive(false);
                ClearLocalModifiers();
                TryRequestPeerClearModifiers();
            }
        }
        catch (Exception ex)
        {
            _forwarder?.Stop();
            _clip?.Release();
            ClearLocalModifiers();
            TryRequestPeerClearModifiers();
            MarkRemoteDisconnected(ex);
            Console.WriteLine($"toggle FAILED: {ex.Message}");
        }
    }

    private static string ResolveRemoteGrpcUrl()
    {
        var configured = Environment.GetEnvironmentVariable("MKP_REMOTE_GRPC");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        if (!string.IsNullOrWhiteSpace(_activeRemoteGrpcUrl))
        {
            return _activeRemoteGrpcUrl;
        }

        // FR-MKP-006: operator settings (mkp settings set remoteGrpcUrl …) before lab defaults.
        try
        {
            var settings = SettingsStore.Load(SettingsStore.DefaultPath());
            if (!string.IsNullOrWhiteSpace(settings.RemoteGrpcUrl))
            {
                return settings.RemoteGrpcUrl.Trim();
            }
        }
        catch
        {
            /* ignore corrupt settings */
        }

        var (_, remotePeer) = LabTopology.ResolvePeers();
        var url = LabTopology.GrpcUrl(remotePeer);
        // No remote configured yet (standalone/unpaired): keep the legacy lab default until a
        // settings-backed remote lands. This URL is only used when nothing is paired.
        return string.IsNullOrWhiteSpace(url) ? LabTopology.GrpcUrl(LabTopology.Desktop) : url;
    }

    /// <summary>
    /// Persists the operator remote endpoint so ToFU / code pairing targets the intended device
    /// (e.g. Pi at https://192.168.1.200:50051) instead of the lab desktop default.
    /// </summary>
    private static void PersistRemoteEndpoint(string remoteGrpcUrl)
    {
        if (string.IsNullOrWhiteSpace(remoteGrpcUrl))
        {
            return;
        }

        try
        {
            var path = SettingsStore.DefaultPath();
            var settings = SettingsStore.Load(path);
            settings.RemoteGrpcUrl = remoteGrpcUrl.Trim();
            if (Uri.TryCreate(settings.RemoteGrpcUrl, UriKind.Absolute, out var uri) &&
                !string.IsNullOrWhiteSpace(uri.Host))
            {
                settings.RemotePeer = uri.Host;
            }

            SettingsStore.Save(path, settings);
        }
        catch
        {
            /* best effort */
        }
    }

    private enum RemoteConnectionState
    {
        NotPaired,
        NotConnected,
        Connected
    }

    private sealed record RemoteActionBinding(string EnabledText, ToolStripItem? MenuItem, Button? Button)
    {
        public static RemoteActionBinding ForMenuItem(ToolStripItem menuItem, string enabledText)
        {
            return new RemoteActionBinding(enabledText, menuItem, null);
        }

        public static RemoteActionBinding ForButton(Button button, string enabledText)
        {
            return new RemoteActionBinding(enabledText, null, button);
        }
    }

    private sealed class PersistedPairingState
    {
        public string RemotePeer { get; set; } = string.Empty;
        public string RemoteGrpcUrl { get; set; } = string.Empty;
        public string PairingCode { get; set; } = string.Empty;
        public bool Connected { get; set; }
        public DateTimeOffset SavedAtUtc { get; set; }
    }

    private sealed class HotkeyMessageForm : Form
    {
        private const int WM_HOTKEY = 0x0312;
        private readonly Action _onHotkey;

        public HotkeyMessageForm(Action onHotkey)
        {
            _onHotkey = onHotkey ?? throw new ArgumentNullException(nameof(onHotkey));
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            Size = new Size(1, 1);
        }

        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(false);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                _onHotkey();
            }

            base.WndProc(ref m);
        }
    }

    // async void: WinForms event handler; awaiting keeps the UI responsive during the inject RPC.
    private static async void DoRealInject(string? text = null)
    {
        if (!EnsureConnectedRemoteAction("Inject Text to Remote"))
        {
            return;
        }

        string payload = text ?? "real-injected";
        string baseUrl = ResolveRemoteGrpcUrl();
        try
        {
            // TR-MKP-AGENTIPC-001: dispatch through the shared command implementation (same code the REPL uses).
            using var channel = CreateRemoteChannel(baseUrl);
            var commands = new RemoteServiceCommands(() => channel is null ? null : new Wire.MouseKeyProxy.MouseKeyProxyClient(channel));
            var result = await commands.InjectTextAsync(payload);
            Console.WriteLine(result.Ok
                ? "[REAL bidi via transport] inject-text sent as SessionFrame/InputBatch SUCCESS"
                : $"[SHIPPED observable fail] inject FAILED: {result.ErrorCode}");
        }
        catch (Exception ex)
        {
            MarkRemoteDisconnected(ex);
            Console.WriteLine($"[SHIPPED observable fail] inject FAILED: {ex.Message}");
        }
    }
}
