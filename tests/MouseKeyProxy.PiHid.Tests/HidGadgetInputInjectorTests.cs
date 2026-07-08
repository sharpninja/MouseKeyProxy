using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MouseKeyProxy.Common;
using MouseKeyProxy.PiHid;
using Xunit;

namespace MouseKeyProxy.PiHid.Tests;

/// <summary>
/// FR-MKP-012 / TR-MKP-HID-001: verifies HidGadgetInputInjector writes encoded reports to the correct
/// gadget device via IHidReportWriter, and surfaces encoder/writer errors as a failed batch.
/// </summary>
public class HidGadgetInputInjectorTests
{
    private sealed class RecordingWriter : IHidReportWriter
    {
        public List<(string Device, byte[] Bytes)> Writes { get; } = new();
        public string KeyboardDevice => "/dev/hidg0";
        public string MouseDevice => "/dev/hidg1";
        public Task WriteKeyboardAsync(byte[] report, CancellationToken cancellationToken = default)
        {
            Writes.Add(("kbd", report));
            return Task.CompletedTask;
        }

        public Task WriteMouseAsync(byte[] report, CancellationToken cancellationToken = default)
        {
            Writes.Add(("mouse", report));
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingWriter : IHidReportWriter
    {
        public string KeyboardDevice => "/dev/hidg0";
        public string MouseDevice => "/dev/hidg1";
        public Task WriteKeyboardAsync(byte[] report, CancellationToken cancellationToken = default) =>
            throw new System.IO.IOException("device busy");
        public Task WriteMouseAsync(byte[] report, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>A key press is written to the keyboard device with the correct usage byte.</summary>
    [Fact]
    [Trait("Category", "HID")]
    public void InjectKey_WritesKeyboardReport()
    {
        var writer = new RecordingWriter();
        var injector = new HidGadgetInputInjector(writer);

        var ok = injector.TryInjectBatch(new[] { new InputEvent(InputKind.KEY_DOWN, Vk: 0x41) }, out var error);

        Assert.True(ok);
        Assert.Null(error);
        var write = Assert.Single(writer.Writes);
        Assert.Equal("kbd", write.Device);
        Assert.Equal(0x04, write.Bytes[2]);
    }

    /// <summary>A relative mouse move is written to the mouse device.</summary>
    [Fact]
    [Trait("Category", "HID")]
    public void InjectMouseMove_WritesMouseReport()
    {
        var writer = new RecordingWriter();
        var injector = new HidGadgetInputInjector(writer);

        var ok = injector.TryInjectBatch(new[] { new InputEvent(InputKind.MOUSE_MOVE, Dx: 5, Dy: -3) }, out _);

        Assert.True(ok);
        var write = Assert.Single(writer.Writes);
        Assert.Equal("mouse", write.Device);
        Assert.Equal(5, (sbyte)write.Bytes[1]);
        Assert.Equal(-3, (sbyte)write.Bytes[2]);
    }

    /// <summary>An unsupported key fails the batch and writes nothing.</summary>
    [Fact]
    [Trait("Category", "HID")]
    public void UnsupportedKey_FailsBatch_NoWrites()
    {
        var writer = new RecordingWriter();
        var injector = new HidGadgetInputInjector(writer);

        var ok = injector.TryInjectBatch(new[] { new InputEvent(InputKind.KEY_DOWN, Vk: 0x00FE) }, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Empty(writer.Writes);
    }

    /// <summary>A writer failure is surfaced as a failed batch with the error message.</summary>
    [Fact]
    [Trait("Category", "HID")]
    public void WriterThrows_FailsBatch()
    {
        var injector = new HidGadgetInputInjector(new ThrowingWriter());

        var ok = injector.TryInjectBatch(new[] { new InputEvent(InputKind.KEY_DOWN, Vk: 0x41) }, out var error);

        Assert.False(ok);
        Assert.Contains("device busy", error);
    }
}
