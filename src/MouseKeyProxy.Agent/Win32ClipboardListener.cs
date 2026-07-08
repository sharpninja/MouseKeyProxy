using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Windows.Forms;
using MouseKeyProxy.Common;

namespace MouseKeyProxy.Agent;

/// <summary>
/// FR-MKP-004 / TR-MKP-CLIP-001: captures local OS clipboard changes via AddClipboardFormatListener +
/// WM_CLIPBOARDUPDATE on a hidden message window (mirrors the RawMouseInputWindow pattern) and raises
/// <see cref="ClipboardCaptured"/> with the captured entry for forwarding to the peer.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Win32ClipboardListener : IClipboardListener, IDisposable
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    private ListenerWindow? _window;
    private ulong _seq;

    /// <inheritdoc />
    public event EventHandler<ClipboardEventArgs>? ClipboardCaptured;

    /// <inheritdoc />
    public void StartListening()
    {
        if (_window is not null)
        {
            return;
        }

        _window = new ListenerWindow(OnClipboardUpdate);
        AddClipboardFormatListener(_window.Handle);
    }

    /// <inheritdoc />
    public void StopListening()
    {
        if (_window is null)
        {
            return;
        }

        RemoveClipboardFormatListener(_window.Handle);
        _window.Dispose();
        _window = null;
    }

    private void OnClipboardUpdate()
    {
        try
        {
            var formats = new List<ClipboardFormat>();
            if (Clipboard.ContainsText())
            {
                formats.Add(new ClipboardFormat("UNICODETEXT", Encoding.UTF8.GetBytes(Clipboard.GetText())));
                if (Clipboard.TryGetData(DataFormats.Html, out string? html) && !string.IsNullOrEmpty(html))
                {
                    formats.Add(new ClipboardFormat("HTML", Encoding.UTF8.GetBytes(html)));
                }
            }

            if (formats.Count == 0)
            {
                return;
            }

            var entry = new ClipboardEntry(
                Guid.NewGuid().ToString("N"),
                DateTimeOffset.UtcNow,
                Environment.MachineName.ToLowerInvariant(),
                formats,
                ++_seq);
            ClipboardCaptured?.Invoke(this, new ClipboardEventArgs(entry));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"MouseKeyProxy clipboard capture failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public void Dispose() => StopListening();

    private sealed class ListenerWindow : NativeWindow, IDisposable
    {
        private readonly Action _onUpdate;

        public ListenerWindow(Action onUpdate)
        {
            _onUpdate = onUpdate;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_CLIPBOARDUPDATE)
            {
                _onUpdate();
            }

            base.WndProc(ref m);
        }

        public void Dispose() => DestroyHandle();
    }
}
