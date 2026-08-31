// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace OmniTray.Core;

[Flags]
public enum VirtualStackCapabilities
{
    None = 0,
    Read = 1,
    Write = 2,
    Remove = 4
}

public sealed record VirtualStackSource
{
    private VirtualStackSource(
        string providerId,
        string? configuration,
        VirtualStackCapabilities capabilities)
    {
        this.ProviderId = providerId;
        this.Configuration = configuration;
        this.Capabilities = capabilities;
    }

    public string ProviderId { get; }

    public string? Configuration { get; }

    public VirtualStackCapabilities Capabilities { get; }

    public bool Can(VirtualStackCapabilities capability) =>
        (this.Capabilities & capability) == capability;

    public static VirtualStackSource Create(
        string providerId,
        string? configuration,
        VirtualStackCapabilities capabilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        return new(providerId.Trim(), configuration?.Trim(), capabilities);
    }
}
