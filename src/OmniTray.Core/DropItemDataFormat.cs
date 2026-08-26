// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace OmniTray.Core;

public enum DropItemDataFormatKind
{
    Text,
    Binary
}

public sealed class DropItemDataFormat
{
    private readonly byte[]? _binaryData;

    private DropItemDataFormat(
        string formatId,
        DropItemDataFormatKind kind,
        string? text,
        byte[]? binaryData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formatId);
        this.FormatId = formatId;
        this.Kind = kind;
        this.Text = text;
        this._binaryData = binaryData is null ? null : [.. binaryData];
    }

    public string FormatId { get; }

    public DropItemDataFormatKind Kind { get; }

    public string? Text { get; }

    public int ByteLength => this._binaryData?.Length ?? 0;

    public static DropItemDataFormat CreateText(string formatId, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new DropItemDataFormat(formatId, DropItemDataFormatKind.Text, text, null);
    }

    public static DropItemDataFormat CreateBinary(string formatId, byte[] binaryData)
    {
        ArgumentNullException.ThrowIfNull(binaryData);
        return new DropItemDataFormat(formatId, DropItemDataFormatKind.Binary, null, binaryData);
    }

    public byte[] GetBinaryData()
    {
        if (this.Kind != DropItemDataFormatKind.Binary || this._binaryData is null)
        {
            throw new InvalidOperationException("The data format does not contain binary data.");
        }

        return [.. this._binaryData];
    }
}
