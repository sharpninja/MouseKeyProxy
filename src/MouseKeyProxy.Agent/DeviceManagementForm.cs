using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Grpc.Net.Client;
using MouseKeyProxy.Commands;
using Wire = MouseKeyProxy.Network.V1;

namespace MouseKeyProxy.Agent;

/// <summary>
/// FR-MKP-013 / FR-MKP-014 / FR-MKP-016 / FR-MKP-018 / FR-MKP-023:
/// complete paired-device configuration surface (HID, media staging, share, SMB, events, pairing assist).
/// </summary>
internal sealed class DeviceManagementForm : Form
{
    private readonly Func<GrpcChannel?> _channelFactory;
    private readonly string _peerId;
    private readonly string _remoteHostHint;
    private readonly Action? _onUnpairLocal;

    private readonly Label _header;
    private readonly TextBox _status;

    // Functions
    private readonly CheckBox _kb;
    private readonly CheckBox _mouse;
    private readonly CheckBox _fs;
    private readonly ComboBox _fsAccess;
    private readonly CheckBox _cdrom;
    private readonly CheckBox _floppy;
    private readonly Button _btnApply;
    private readonly Button _btnRefresh;

    // Media
    private readonly ComboBox _cdromSource;
    private readonly TextBox _cdromPath;
    private readonly ComboBox _floppySource;
    private readonly TextBox _floppyPath;

    // Share
    private readonly Label _shareInfo;
    private readonly Label _allowlistInfo;
    private readonly TextBox _smbUnc;
    private readonly ListBox _shareList;
    private string _shareDir = string.Empty;

    // Events
    private readonly ListBox _events;

    // Pairing
    private readonly TextBox _issuedCode;
    private readonly TextBox _enterCode;
    private readonly Label _pairStatus;

    private readonly TabControl _tabs;

    /// <summary>Creates the complete device management form.</summary>
    /// <param name="channelFactory">Factory that returns an authenticated channel to the paired device.</param>
    /// <param name="peerId">Local peer id for RPC headers.</param>
    /// <param name="remoteHostHint">Optional host/IP or gRPC URL for SMB UNC construction.</param>
    /// <param name="initialTab">Optional tab to select (0=Functions … 4=Pairing).</param>
    /// <param name="onUnpairLocal">Optional callback to clear Agent local pairing state after remote Unpair.</param>
    public DeviceManagementForm(
        Func<GrpcChannel?> channelFactory,
        string peerId,
        string? remoteHostHint = null,
        int initialTab = 0,
        Action? onUnpairLocal = null)
    {
        _channelFactory = channelFactory ?? throw new ArgumentNullException(nameof(channelFactory));
        _peerId = string.IsNullOrWhiteSpace(peerId) ? Environment.MachineName : peerId;
        _remoteHostHint = remoteHostHint ?? string.Empty;
        _onUnpairLocal = onUnpairLocal;

        Text = "Paired device configuration";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(720, 640);
        Size = new Size(800, 720);
        Padding = new Padding(10);
        Font = SystemFonts.MessageBoxFont ?? Control.DefaultFont;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        Controls.Add(root);

        _header = new Label
        {
            AutoSize = true,
            Margin = new Padding(4),
            Text = BuildHeaderText(),
        };
        root.Controls.Add(_header, 0, 0);

        _tabs = new TabControl { Dock = DockStyle.Fill };
        root.Controls.Add(_tabs, 0, 1);

        // --- Functions ---
        var tabFunctions = new TabPage("Functions");
        var funcLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(8),
            AutoSize = true,
        };
        funcLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        funcLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        _kb = new CheckBox { Text = "Keyboard HID", AutoSize = true, Margin = new Padding(4) };
        _mouse = new CheckBox { Text = "Mouse HID", AutoSize = true, Margin = new Padding(4) };
        _fs = new CheckBox { Text = "Disk FS (mass storage LUN 0)", AutoSize = true, Margin = new Padding(4) };
        _fsAccess = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 140,
            Margin = new Padding(4),
        };
        _fsAccess.Items.AddRange(new object[] { "Read-only", "Read-write" });
        _fsAccess.SelectedIndex = 0;
        _cdrom = new CheckBox { Text = "CD-ROM LUN", AutoSize = true, Margin = new Padding(4) };
        _floppy = new CheckBox { Text = "Virtual floppy LUN", AutoSize = true, Margin = new Padding(4) };
        funcLayout.Controls.Add(_kb, 0, 0);
        funcLayout.Controls.Add(_mouse, 1, 0);
        funcLayout.Controls.Add(_fs, 0, 1);
        funcLayout.Controls.Add(_fsAccess, 1, 1);
        funcLayout.Controls.Add(_cdrom, 0, 2);
        funcLayout.Controls.Add(_floppy, 1, 2);
        var funcActions = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        _btnRefresh = new Button { Text = "Refresh", AutoSize = true, Margin = new Padding(4) };
        _btnApply = new Button { Text = "Apply configuration", AutoSize = true, Margin = new Padding(4) };
        _btnRefresh.Click += async (_, _) => await RefreshAllAsync().ConfigureAwait(true);
        _btnApply.Click += async (_, _) => await ApplyAsync().ConfigureAwait(true);
        funcActions.Controls.Add(_btnRefresh);
        funcActions.Controls.Add(_btnApply);
        funcLayout.Controls.Add(funcActions, 0, 3);
        funcLayout.SetColumnSpan(funcActions, 2);
        var funcHelp = new Label
        {
            AutoSize = true,
            Margin = new Padding(4, 12, 4, 4),
            MaximumSize = new Size(700, 0),
            Text = "Enable or disable USB gadget functions on the paired appliance. Media paths are set on the Media tab. Apply sends ConfigureDevice over mTLS.",
        };
        funcLayout.Controls.Add(funcHelp, 0, 4);
        funcLayout.SetColumnSpan(funcHelp, 2);
        tabFunctions.Controls.Add(funcLayout);
        _tabs.TabPages.Add(tabFunctions);

        // --- Media ---
        var tabMedia = new TabPage("Media");
        var mediaLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 4,
            Padding = new Padding(8),
        };
        _cdromSource = MediaSourceCombo();
        _cdromPath = new TextBox { Width = 320, Margin = new Padding(4), PlaceholderText = "device path or host media name" };
        _floppySource = MediaSourceCombo();
        _floppyPath = new TextBox { Width = 320, Margin = new Padding(4), PlaceholderText = "device path or host media name" };
        mediaLayout.Controls.Add(new Label { Text = "CD-ROM media", AutoSize = true, Margin = new Padding(4) }, 0, 0);
        mediaLayout.Controls.Add(_cdromSource, 1, 0);
        mediaLayout.Controls.Add(_cdromPath, 2, 0);
        var btnStageCd = new Button { Text = "Stage host file…", AutoSize = true, Margin = new Padding(4) };
        btnStageCd.Click += async (_, _) => await StageHostMediaAsync(forCdrom: true).ConfigureAwait(true);
        mediaLayout.Controls.Add(btnStageCd, 3, 0);
        mediaLayout.Controls.Add(new Label { Text = "Floppy media", AutoSize = true, Margin = new Padding(4) }, 0, 1);
        mediaLayout.Controls.Add(_floppySource, 1, 1);
        mediaLayout.Controls.Add(_floppyPath, 2, 1);
        var btnStageFl = new Button { Text = "Stage host file…", AutoSize = true, Margin = new Padding(4) };
        btnStageFl.Click += async (_, _) => await StageHostMediaAsync(forCdrom: false).ConfigureAwait(true);
        mediaLayout.Controls.Add(btnStageFl, 3, 1);
        var mediaHelp = new Label
        {
            AutoSize = true,
            Margin = new Padding(4, 12, 4, 4),
            MaximumSize = new Size(720, 0),
            Text = "Device = path on the appliance (e.g. under MKP-DEPLOY/media/device). Host = name under media/host after Stage (upload via folder share, then Apply).",
        };
        mediaLayout.Controls.Add(mediaHelp, 0, 2);
        mediaLayout.SetColumnSpan(mediaHelp, 4);
        var mediaApply = new Button { Text = "Apply media + functions", AutoSize = true, Margin = new Padding(4) };
        mediaApply.Click += async (_, _) => await ApplyAsync(updateMedia: true).ConfigureAwait(true);
        mediaLayout.Controls.Add(mediaApply, 0, 3);
        tabMedia.Controls.Add(mediaLayout);
        _tabs.TabPages.Add(tabMedia);

        // --- Share ---
        var tabShare = new TabPage("Share");
        var shareRoot = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(8),
        };
        shareRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shareRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shareRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shareRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shareRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        _shareInfo = new Label { AutoSize = true, Margin = new Padding(4) };
        _allowlistInfo = new Label { AutoSize = true, Margin = new Padding(4), ForeColor = SystemColors.GrayText };
        _smbUnc = new TextBox { ReadOnly = true, Dock = DockStyle.Top, Margin = new Padding(4) };
        var shareButtons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        var btnShareRefresh = new Button { Text = "Refresh", AutoSize = true, Margin = new Padding(4) };
        var btnShareUp = new Button { Text = "Up", AutoSize = true, Margin = new Padding(4) };
        var btnShareGet = new Button { Text = "Download…", AutoSize = true, Margin = new Padding(4) };
        var btnSharePut = new Button { Text = "Upload…", AutoSize = true, Margin = new Padding(4) };
        var btnOpenSmb = new Button { Text = "Open SMB…", AutoSize = true, Margin = new Padding(4) };
        var btnCopyUnc = new Button { Text = "Copy UNC", AutoSize = true, Margin = new Padding(4) };
        btnShareRefresh.Click += async (_, _) => await RefreshShareAsync().ConfigureAwait(true);
        btnShareUp.Click += async (_, _) =>
        {
            _shareDir = ParentRelativeDir(_shareDir);
            await RefreshShareAsync().ConfigureAwait(true);
        };
        btnShareGet.Click += async (_, _) => await DownloadSelectedAsync().ConfigureAwait(true);
        btnSharePut.Click += async (_, _) => await UploadFileAsync().ConfigureAwait(true);
        btnOpenSmb.Click += (_, _) => OpenSmbShare();
        btnCopyUnc.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_smbUnc.Text))
            {
                Clipboard.SetText(_smbUnc.Text);
                SetStatus("UNC copied to clipboard.");
            }
        };
        shareButtons.Controls.Add(btnShareRefresh);
        shareButtons.Controls.Add(btnShareUp);
        shareButtons.Controls.Add(btnShareGet);
        shareButtons.Controls.Add(btnSharePut);
        shareButtons.Controls.Add(btnOpenSmb);
        shareButtons.Controls.Add(btnCopyUnc);
        _shareList = new ListBox { Dock = DockStyle.Fill };
        _shareList.DoubleClick += async (_, _) => await OpenSelectedShareEntryAsync().ConfigureAwait(true);
        shareRoot.Controls.Add(_shareInfo, 0, 0);
        shareRoot.Controls.Add(_allowlistInfo, 0, 1);
        shareRoot.Controls.Add(_smbUnc, 0, 2);
        shareRoot.Controls.Add(shareButtons, 0, 3);
        shareRoot.Controls.Add(_shareList, 0, 4);
        tabShare.Controls.Add(shareRoot);
        _tabs.TabPages.Add(tabShare);

        // --- Events ---
        var tabEvents = new TabPage("Events");
        _events = new ListBox { Dock = DockStyle.Fill };
        var eventsPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        eventsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        eventsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        var eventsHelp = new Label
        {
            AutoSize = true,
            Margin = new Padding(8),
            Text = "Device lifecycle events from the last Apply (and refresh notes). Full session-stream mirror is server-side.",
        };
        eventsPanel.Controls.Add(eventsHelp, 0, 0);
        eventsPanel.Controls.Add(_events, 0, 1);
        tabEvents.Controls.Add(eventsPanel);
        _tabs.TabPages.Add(tabEvents);

        // --- Pairing ---
        var tabPair = new TabPage("Pairing");
        var pairLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Padding = new Padding(8),
        };
        pairLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pairLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        var pairHelp = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(700, 0),
            Margin = new Padding(4),
            Text = "Issue a one-time code on this paired device for a new client (typed on the connecting machine). " +
                   "First USB peer may use ToFU; additional clients require the code (FR-MKP-023).",
        };
        pairLayout.Controls.Add(pairHelp, 0, 0);
        pairLayout.SetColumnSpan(pairHelp, 2);

        var btnIssue = new Button { Text = "Issue pairing code…", AutoSize = true, Margin = new Padding(4) };
        btnIssue.Click += async (_, _) => await IssuePairingCodeAsync().ConfigureAwait(true);
        pairLayout.Controls.Add(btnIssue, 0, 1);
        _issuedCode = new TextBox { ReadOnly = true, Width = 200, Margin = new Padding(4), Font = new Font(Font, FontStyle.Bold) };
        pairLayout.Controls.Add(_issuedCode, 1, 1);

        pairLayout.Controls.Add(new Label { Text = "Enter code (this machine as new client)", AutoSize = true, Margin = new Padding(4) }, 0, 2);
        _enterCode = new TextBox { Width = 200, Margin = new Padding(4), PlaceholderText = "6-digit code" };
        pairLayout.Controls.Add(_enterCode, 1, 2);
        var btnPair = new Button { Text = "Complete pair with code…", AutoSize = true, Margin = new Padding(4) };
        btnPair.Click += async (_, _) => await CompletePairWithCodeAsync().ConfigureAwait(true);
        pairLayout.Controls.Add(btnPair, 0, 3);
        var btnUnpair = new Button { Text = "Unpair this PC…", AutoSize = true, Margin = new Padding(4) };
        btnUnpair.Click += async (_, _) => await UnpairThisPcAsync().ConfigureAwait(true);
        pairLayout.Controls.Add(btnUnpair, 1, 3);
        _pairStatus = new Label { AutoSize = true, Margin = new Padding(4), MaximumSize = new Size(700, 0) };
        pairLayout.Controls.Add(_pairStatus, 0, 4);
        pairLayout.SetColumnSpan(_pairStatus, 2);
        tabPair.Controls.Add(pairLayout);
        _tabs.TabPages.Add(tabPair);

        if (initialTab >= 0 && initialTab < _tabs.TabCount)
        {
            _tabs.SelectedIndex = initialTab;
        }

        _status = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Vertical,
        };
        root.Controls.Add(_status, 0, 2);

        Shown += async (_, _) => await RefreshAllAsync().ConfigureAwait(true);
        UpdatePairedControlsEnabled();
    }

    private string BuildHeaderText()
    {
        var host = string.IsNullOrWhiteSpace(_remoteHostHint) ? "(no remote URL)" : _remoteHostHint;
        return $"Peer id: {_peerId}   Remote: {host}";
    }

    private static ComboBox MediaSourceCombo()
    {
        var c = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 100,
            Margin = new Padding(4),
        };
        c.Items.AddRange(new object[] { "Device", "Host" });
        c.SelectedIndex = 0;
        return c;
    }

    private Wire.MouseKeyProxy.MouseKeyProxyClient? CreateClient(out string? error)
    {
        error = null;
        var channel = _channelFactory();
        if (channel is null)
        {
            error = "No authenticated channel to the paired device. Pair and connect first.";
            return null;
        }

        return new Wire.MouseKeyProxy.MouseKeyProxyClient(channel);
    }

    private void UpdatePairedControlsEnabled()
    {
        var ok = _channelFactory() is not null;
        _btnApply.Enabled = ok;
        _btnRefresh.Enabled = ok;
        if (!ok)
        {
            SetStatus("Not paired / no channel. Pair and connect to manage device configuration.");
        }
    }

    private async Task RefreshAllAsync()
    {
        UpdatePairedControlsEnabled();
        await RefreshAsync().ConfigureAwait(true);
        await RefreshShareAsync().ConfigureAwait(true);
    }

    private async Task RefreshAsync()
    {
        var client = CreateClient(out var error);
        if (client is null)
        {
            SetStatus(error!);
            return;
        }

        try
        {
            var response = await client.GetDeviceConfigurationAsync(new Wire.GetDeviceConfigurationRequest
            {
                ProtocolVersion = "v1",
                PeerId = _peerId,
                CorrelationId = Guid.NewGuid().ToString("n"),
            }).ResponseAsync.ConfigureAwait(true);

            if (!response.Ok || response.State is null)
            {
                SetStatus($"GetDeviceConfiguration failed: {response.Err} {response.Msg}");
                if (string.Equals(response.Err, "PLATFORM_NOT_SUPPORTED", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(this,
                        "This peer does not expose USB gadget configuration (PLATFORM_NOT_SUPPORTED).\n\n" +
                        "Pair to the Pi appliance (e.g. https://192.168.1.200:50051 from Discover folder shares), " +
                        "not a Windows lab peer like payton-desktop.",
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                return;
            }

            var model = DeviceConfigViewModel.FromState(response.State);
            ApplyModelToControls(model);
            RefreshSmbUnc();
            SetStatus(
                $"Loaded: kb={model.KeyboardEnabled} mouse={model.MouseEnabled} fs={model.FsEnabled} " +
                $"cdrom={model.CdromEnabled} floppy={model.FloppyEnabled}");
        }
        catch (Exception ex)
        {
            var msg = ex.Message;
            // gRPC Unimplemented = remote binary has no ConfigureDevice/GetDeviceConfiguration
            // (Windows service, or outdated Pi service). Rufus seed only lives on the Pi.
            if (msg.Contains("Unimplemented", StringComparison.OrdinalIgnoreCase))
            {
                SetStatus(
                    "Refresh failed: remote does not implement device configuration (Unimplemented). " +
                    $"Currently connected to: {_remoteHostHint}. " +
                    "Pair to the Pi (Discover folder shares → https://192.168.1.200:50051), " +
                    "not payton-desktop. Rufus first-boot defaults are only on the Pi (/etc/mkp/seed.json).");
            }
            else
            {
                SetStatus($"Refresh failed: {msg}");
            }
        }
    }

    private void RefreshSmbUnc()
    {
        if (string.IsNullOrWhiteSpace(_remoteHostHint))
        {
            return;
        }

        try
        {
            _smbUnc.Text = DeviceConfigViewModel.BuildSmbUnc(_remoteHostHint);
        }
        catch
        {
            /* leave empty */
        }
    }

    private void ApplyModelToControls(DeviceConfigUiModel model)
    {
        _kb.Checked = model.KeyboardEnabled;
        _mouse.Checked = model.MouseEnabled;
        _fs.Checked = model.FsEnabled;
        _fsAccess.SelectedIndex = model.FsReadWrite ? 1 : 0;
        _cdrom.Checked = model.CdromEnabled;
        _cdromSource.SelectedIndex = model.CdromFromHost ? 1 : 0;
        _cdromPath.Text = model.CdromPath;
        _floppy.Checked = model.FloppyEnabled;
        _floppySource.SelectedIndex = model.FloppyFromHost ? 1 : 0;
        _floppyPath.Text = model.FloppyPath;
    }

    private DeviceConfigUiModel ReadModelFromControls(bool updateMedia) => new()
    {
        KeyboardEnabled = _kb.Checked,
        MouseEnabled = _mouse.Checked,
        FsEnabled = _fs.Checked,
        FsReadWrite = _fsAccess.SelectedIndex == 1,
        CdromEnabled = _cdrom.Checked,
        CdromFromHost = _cdromSource.SelectedIndex == 1,
        CdromPath = _cdromPath.Text,
        UpdateCdromMedia = updateMedia,
        FloppyEnabled = _floppy.Checked,
        FloppyFromHost = _floppySource.SelectedIndex == 1,
        FloppyPath = _floppyPath.Text,
        UpdateFloppyMedia = updateMedia,
    };

    private async Task ApplyAsync(bool updateMedia = false)
    {
        var client = CreateClient(out var error);
        if (client is null)
        {
            SetStatus(error!);
            return;
        }

        try
        {
            // From Functions tab, only push media when paths are non-empty and user was on Media.
            // Media tab always updateMedia=true.
            var model = ReadModelFromControls(updateMedia);
            if (!updateMedia &&
                (!string.IsNullOrWhiteSpace(_cdromPath.Text) || !string.IsNullOrWhiteSpace(_floppyPath.Text)))
            {
                // Keep prior media if not explicitly updating from Media tab.
            }

            var request = DeviceConfigViewModel.ToConfigureRequest(model, _peerId);
            var response = await client.ConfigureDeviceAsync(request).ResponseAsync.ConfigureAwait(true);
            AppendEvents(response.Events);

            SetStatus(response.Ok
                ? $"Applied OK: {response.Msg} ({response.Events.Count} events)"
                : $"Apply failed: {response.Err} {response.Msg}");

            if (response.Ok && response.State is not null)
            {
                ApplyModelToControls(DeviceConfigViewModel.FromState(response.State));
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Apply failed: {ex.Message}");
        }
    }

    private void AppendEvents(IEnumerable<Wire.DeviceEventMsg> events)
    {
        foreach (var e in events)
        {
            _events.Items.Insert(0, $"{DateTime.Now:HH:mm:ss}  {e.Kind}  {e.Detail}  path={e.Path}");
        }

        while (_events.Items.Count > 200)
        {
            _events.Items.RemoveAt(_events.Items.Count - 1);
        }
    }

    private async Task StageHostMediaAsync(bool forCdrom)
    {
        using var dlg = new OpenFileDialog
        {
            Title = forCdrom ? "Stage CD/ISO media to device host inbox" : "Stage floppy image to device host inbox",
            Filter = forCdrom
                ? "Optical images (*.iso;*.img)|*.iso;*.img|All files (*.*)|*.*"
                : "Disk images (*.img;*.bin)|*.img;*.bin|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var client = CreateClient(out var error);
        if (client is null)
        {
            SetStatus(error!);
            return;
        }

        var name = Path.GetFileName(dlg.FileName);
        // Prefer media/host/ prefix when share is the deploy tree; sandbox will reject if missing.
        var remote = $"media/host/{name}";
        try
        {
            var share = new FolderShareClient(client, _peerId);
            var result = await share.UploadAsync(dlg.FileName, remote).ConfigureAwait(true);
            if (!result.Ok)
            {
                // Fallback: upload to share root with the bare name.
                remote = name;
                result = await share.UploadAsync(dlg.FileName, remote).ConfigureAwait(true);
            }

            if (!result.Ok)
            {
                SetStatus($"Stage failed: {result.ErrorCode} {result.Message}");
                MessageBox.Show(this, result.Message, "Stage host media", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (forCdrom)
            {
                _cdromSource.SelectedIndex = 1; // Host
                _cdromPath.Text = Path.GetFileName(remote);
                _cdrom.Checked = true;
            }
            else
            {
                _floppySource.SelectedIndex = 1;
                _floppyPath.Text = Path.GetFileName(remote);
                _floppy.Checked = true;
            }

            SetStatus($"Staged {name} → {remote}. Applying media configuration…");
            await ApplyAsync(updateMedia: true).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetStatus($"Stage failed: {ex.Message}");
        }
    }

    private async Task RefreshShareAsync()
    {
        var client = CreateClient(out var error);
        if (client is null)
        {
            _shareInfo.Text = error!;
            _allowlistInfo.Text = string.Empty;
            return;
        }

        try
        {
            RefreshSmbUnc();
            var share = new FolderShareClient(client, _peerId);
            var info = await share.GetInfoAsync().ConfigureAwait(true);
            if (!info.Ok)
            {
                _shareInfo.Text = $"Share: unavailable ({info.Err} {info.Msg})";
                _allowlistInfo.Text = string.Equals(info.Err, "SHARE_IP_DENIED", StringComparison.OrdinalIgnoreCase)
                    ? "IP allowlist: DENIED for this host (only UsbConnectedPc + PairedHost IPs may access share/SMB)."
                    : "IP allowlist: unknown (share disabled or error).";
                _shareList.Items.Clear();
                return;
            }

            if (!info.Enabled)
            {
                _shareInfo.Text = "Share: disabled on device";
                _allowlistInfo.Text = string.Empty;
                _shareList.Items.Clear();
                return;
            }

            _shareInfo.Text = $"Share: {info.ShareName}  rw={info.ReadWrite}  root={info.RootLabel}  dir={(_shareDir.Length == 0 ? "/" : _shareDir)}";
            _allowlistInfo.Text = "IP allowlist: allowed for this connection (paired host or USB-connected PC).";
            if (!string.IsNullOrWhiteSpace(info.ShareName) && !string.IsNullOrWhiteSpace(_remoteHostHint))
            {
                try
                {
                    _smbUnc.Text = DeviceConfigViewModel.BuildSmbUnc(_remoteHostHint, info.ShareName);
                }
                catch
                {
                    /* ignore */
                }
            }

            var list = await share.ListAsync(_shareDir).ConfigureAwait(true);
            _shareList.Items.Clear();
            if (!list.Ok)
            {
                _shareList.Items.Add($"list error: {list.Err} {list.Msg}");
                if (string.Equals(list.Err, "SHARE_IP_DENIED", StringComparison.OrdinalIgnoreCase))
                {
                    _allowlistInfo.Text = "IP allowlist: DENIED for this host.";
                }

                return;
            }

            foreach (var e in list.Entries)
            {
                var tag = e.IsDirectory ? "[dir] " : "[file]";
                _shareList.Items.Add($"{tag} {e.RelativePath}{(e.IsDirectory ? string.Empty : $" ({e.SizeBytes} bytes)")}");
            }

            if (list.Entries.Count == 0)
            {
                _shareList.Items.Add("(empty)");
            }
        }
        catch (Exception ex)
        {
            _shareInfo.Text = $"Share refresh failed: {ex.Message}";
        }
    }

    private async Task OpenSelectedShareEntryAsync()
    {
        if (_shareList.SelectedItem is null)
        {
            return;
        }

        var text = _shareList.SelectedItem.ToString() ?? string.Empty;
        if (!text.StartsWith("[dir]", StringComparison.Ordinal))
        {
            return;
        }

        var path = text.Replace("[dir]", string.Empty, StringComparison.Ordinal).Trim();
        _shareDir = path;
        await RefreshShareAsync().ConfigureAwait(true);
    }

    private static string ParentRelativeDir(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir))
        {
            return string.Empty;
        }

        var trimmed = dir.Trim('/').Trim('\\');
        var idx = trimmed.LastIndexOfAny(new[] { '/', '\\' });
        return idx <= 0 ? string.Empty : trimmed[..idx].Replace('\\', '/');
    }

    private async Task DownloadSelectedAsync()
    {
        if (_shareList.SelectedItem is null)
        {
            MessageBox.Show(this, "Select a file entry first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var text = _shareList.SelectedItem.ToString() ?? string.Empty;
        if (text.StartsWith("[dir]", StringComparison.Ordinal) || text.StartsWith("(empty)", StringComparison.Ordinal))
        {
            MessageBox.Show(this, "Select a file, not a directory.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var pathPart = text.Replace("[file]", string.Empty, StringComparison.Ordinal).Trim();
        var paren = pathPart.LastIndexOf(" (", StringComparison.Ordinal);
        if (paren > 0)
        {
            pathPart = pathPart[..paren].Trim();
        }

        using var dlg = new SaveFileDialog
        {
            FileName = Path.GetFileName(pathPart),
            Title = "Save shared file",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var client = CreateClient(out var error);
        if (client is null)
        {
            SetStatus(error!);
            return;
        }

        try
        {
            var share = new FolderShareClient(client, _peerId);
            var result = await share.DownloadAsync(pathPart, dlg.FileName).ConfigureAwait(true);
            SetStatus(result.Ok ? result.Message : $"{result.ErrorCode}: {result.Message}");
        }
        catch (Exception ex)
        {
            SetStatus($"Download failed: {ex.Message}");
        }
    }

    private async Task UploadFileAsync()
    {
        using var dlg = new OpenFileDialog { Title = "Upload to device share" };
        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var remoteName = string.IsNullOrEmpty(_shareDir)
            ? Path.GetFileName(dlg.FileName)
            : $"{_shareDir.TrimEnd('/')}/{Path.GetFileName(dlg.FileName)}";
        var client = CreateClient(out var error);
        if (client is null)
        {
            SetStatus(error!);
            return;
        }

        try
        {
            var share = new FolderShareClient(client, _peerId);
            var result = await share.UploadAsync(dlg.FileName, remoteName).ConfigureAwait(true);
            SetStatus(result.Ok ? result.Message : $"{result.ErrorCode}: {result.Message}");
            if (result.Ok)
            {
                await RefreshShareAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Upload failed: {ex.Message}");
        }
    }

    private async Task IssuePairingCodeAsync()
    {
        var client = CreateClient(out var error);
        if (client is null)
        {
            _pairStatus.Text = error!;
            return;
        }

        try
        {
            var response = await client.RequestPairingCodeAsync(new Wire.RequestPairingCodeRequest
            {
                ProtocolVersion = "v1",
                TtlSeconds = 300,
            }).ResponseAsync.ConfigureAwait(true);

            if (!response.Success || string.IsNullOrWhiteSpace(response.PairingCode))
            {
                _pairStatus.Text = "Failed to issue pairing code.";
                return;
            }

            _issuedCode.Text = response.PairingCode;
            _pairStatus.Text =
                $"Code issued (TTL {response.TtlSeconds}s). Type this code on the connecting machine's Agent " +
                "(Pairing tab or Pairing dialog), not on this host.";
            SetStatus($"Issued pairing code for new client (TTL {response.TtlSeconds}s).");
        }
        catch (Exception ex)
        {
            _pairStatus.Text = $"Issue failed: {ex.Message}";
        }
    }

    private async Task CompletePairWithCodeAsync()
    {
        var code = _enterCode.Text?.Trim() ?? string.Empty;
        // Empty code = ToFU first pair on an unpaired device (MKP_TOFU=1). Non-empty must look like a code.
        if (!string.IsNullOrEmpty(code) && !DeviceConfigViewModel.IsPlausiblePairingCode(code))
        {
            _pairStatus.Text = "Enter a valid pairing code (4–12 characters), or leave empty for ToFU first pair.";
            return;
        }

        if (string.IsNullOrWhiteSpace(_remoteHostHint))
        {
            _pairStatus.Text = "No remote URL available for pairing.";
            return;
        }

        try
        {
            _pairStatus.Text = string.IsNullOrEmpty(code) ? "Pairing (ToFU, no code)…" : "Pairing…";
            var peerId = Environment.MachineName.ToLowerInvariant();
            var credential = await PairingClient.PairAsync(_remoteHostHint, peerId, code).ConfigureAwait(true);
            _pairStatus.Text =
                $"Paired as {credential.PeerId}. Thumbprint {credential.ClientCertificate.Thumbprint}. " +
                "If this Agent was already paired to another peer, use the main Pairing dialog to persist state.";
            SetStatus($"Pair with code succeeded for {credential.PeerId}.");
            credential.ClientCertificate.Dispose();
            credential.CaCertificate.Dispose();
        }
        catch (PairingException ex)
        {
            _pairStatus.Text = $"Pair failed: {ex.Error}";
        }
        catch (Exception ex)
        {
            _pairStatus.Text = $"Pair failed: {ex.Message}";
        }
    }

    private async Task UnpairThisPcAsync()
    {
        var confirm = MessageBox.Show(
            this,
            "Unpair this PC from the remote device?\n\n" +
            "Sends Unpair to the device (if reachable), then clears local credentials on this machine.",
            "Unpair",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes)
        {
            return;
        }

        var remoteMsg = "Remote Unpair skipped.";
        try
        {
            var client = CreateClient(out var error);
            if (client is not null)
            {
                var response = await client.UnpairAsync(new Wire.UnpairRequest
                {
                    ProtocolVersion = "v1",
                    PeerId = string.Empty, // self via client cert
                    CorrelationId = Guid.NewGuid().ToString("n"),
                    ClearAll = false,
                }).ResponseAsync.ConfigureAwait(true);
                remoteMsg = response.Ok
                    ? response.Msg
                    : $"{response.Err} {response.Msg}";
            }
            else
            {
                remoteMsg = error ?? "No channel.";
            }
        }
        catch (Exception ex)
        {
            remoteMsg = ex.Message;
        }

        try
        {
            _onUnpairLocal?.Invoke();
        }
        catch (Exception ex)
        {
            remoteMsg += $" Local clear failed: {ex.Message}";
        }

        _pairStatus.Text = $"Unpaired. {remoteMsg}";
        SetStatus($"Unpaired. {remoteMsg}");
        MessageBox.Show(this, $"Unpaired.\n{remoteMsg}", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        Close();
    }

    private void SetStatus(string text) => _status.Text = text;

    private void OpenSmbShare()
    {
        try
        {
            var unc = string.IsNullOrWhiteSpace(_smbUnc.Text) && !string.IsNullOrWhiteSpace(_remoteHostHint)
                ? DeviceConfigViewModel.BuildSmbUnc(_remoteHostHint)
                : _smbUnc.Text.Trim();
            if (string.IsNullOrWhiteSpace(unc))
            {
                MessageBox.Show(this, "No SMB path available. Pair the device and Refresh first.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _smbUnc.Text = unc;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = unc,
                UseShellExecute = true,
            });
            SetStatus($"Opened {unc}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Open SMB share", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
