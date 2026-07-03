using System;
using System.Collections.Generic;

namespace MouseKeyProxy.Common;

/// <summary>
/// Domain models for LIFO clipboard (pure, matches proto + locked decisions).
/// </summary>
public record ClipboardEntry(
    string Id,
    DateTimeOffset Timestamp,
    string SourcePeer,
    IReadOnlyList<ClipboardFormat> Formats,
    ulong Seq = 0
);

public record ClipboardFormat(string Name, byte[] Data);

public record MergeResult(IReadOnlyList<ClipboardEntry> History, bool Changed);
