using System;
using System.Collections.Generic;
using MouseKeyProxy.Common;

namespace MouseKeyProxy.PiHid;

/// <summary>
/// FR-MKP-012 / TR-MKP-HID-001: an <see cref="IInputInjector"/> for the Linux/Pi USB HID gadget. It
/// encodes arbitrary input-event batches into boot-protocol reports (via <see cref="PiHidEncoder"/>)
/// and writes them to /dev/hidg0 (keyboard) and /dev/hidg1 (mouse) through an
/// <see cref="IHidReportWriter"/>. Batches are serialized so reports never interleave.
/// </summary>
public sealed class HidGadgetInputInjector : IInputInjector
{
    private readonly IHidReportWriter _writer;
    private readonly PiHidEncoder _encoder = new();
    private readonly object _gate = new();

    /// <summary>Creates the injector over a HID report writer.</summary>
    /// <param name="writer">The report writer targeting the gadget devices.</param>
    public HidGadgetInputInjector(IHidReportWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    /// <inheritdoc />
    public void Send(InputEvent evt)
    {
        if (!TryInjectBatch(new[] { evt }, out var error))
        {
            throw new InvalidOperationException(error ?? "HID injection failed.");
        }
    }

    /// <inheritdoc />
    public bool TryInjectBatch(IEnumerable<InputEvent> events, out string? error)
    {
        lock (_gate)
        {
            var reports = _encoder.Encode(events, out error);
            if (error is not null)
            {
                return false;
            }

            try
            {
                foreach (var report in reports)
                {
                    if (report.Device == HidDevice.Keyboard)
                    {
                        _writer.WriteKeyboardAsync(report.Bytes).GetAwaiter().GetResult();
                    }
                    else
                    {
                        _writer.WriteMouseAsync(report.Bytes).GetAwaiter().GetResult();
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            return true;
        }
    }
}
