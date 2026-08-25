// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using Windows.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml.Media;

namespace OmniTray.Services;

internal sealed partial class TintedAcrylicBackdrop : SystemBackdrop
{
    private DesktopAcrylicController? _controller;
    private Color _tintColor;
    private XamlRoot? _xamlRoot;

    public TintedAcrylicBackdrop(Color tintColor)
    {
        this._tintColor = tintColor;
    }

    public static bool IsSupported => DesktopAcrylicController.IsSupported();

    public Color TintColor
    {
        get => this._tintColor;
        set
        {
            this._tintColor = value;
            this.ApplyAppearance();
        }
    }

    public float TintOpacity { get; set; } = 0.24f;

    public float LuminosityOpacity { get; set; } = 0.78f;

    protected override void OnTargetConnected(
        ICompositionSupportsSystemBackdrop connectedTarget,
        XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);

        if (this._controller is not null)
        {
            throw new InvalidOperationException("A tinted Acrylic backdrop instance cannot be shared.");
        }

        this._xamlRoot = xamlRoot;
        var controller = new DesktopAcrylicController { Kind = DesktopAcrylicKind.Thin };

        try
        {
            this._controller = controller;
            this.ApplyAppearance();
            controller.SetSystemBackdropConfiguration(
                this.GetDefaultSystemBackdropConfiguration(connectedTarget, xamlRoot));
            controller.AddSystemBackdropTarget(connectedTarget);
        }
        catch
        {
            this._controller = null;
            this._xamlRoot = null;
            controller.Dispose();
            throw;
        }
    }

    protected override void OnDefaultSystemBackdropConfigurationChanged(
        ICompositionSupportsSystemBackdrop target,
        XamlRoot xamlRoot)
    {
        base.OnDefaultSystemBackdropConfigurationChanged(target, xamlRoot);

        this._xamlRoot = xamlRoot;
        if (this._controller is not null)
        {
            this._controller.SetSystemBackdropConfiguration(
                this.GetDefaultSystemBackdropConfiguration(target, xamlRoot));
            this.ApplyAppearance();
        }
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);

        var controller = this._controller;
        this._controller = null;
        this._xamlRoot = null;
        if (controller is null)
        {
            return;
        }

        try
        {
            controller.RemoveSystemBackdropTarget(disconnectedTarget);
        }
        finally
        {
            controller.Dispose();
        }
    }

    public static Color CreateFallbackColor(Color tintColor, ElementTheme theme)
    {
        var baseColor = theme == ElementTheme.Light
            ? Color.FromArgb(255, 243, 243, 243)
            : Color.FromArgb(255, 32, 32, 32);
        const float tintAmount = 0.22f;

        return Color.FromArgb(
            255,
            Blend(baseColor.R, tintColor.R, tintAmount),
            Blend(baseColor.G, tintColor.G, tintAmount),
            Blend(baseColor.B, tintColor.B, tintAmount));
    }

    private void ApplyAppearance()
    {
        if (this._controller is null)
        {
            return;
        }

        this._controller.TintColor = this.TintColor;
        this._controller.TintOpacity = this.TintOpacity;
        this._controller.LuminosityOpacity = this.LuminosityOpacity;
        this._controller.FallbackColor = CreateFallbackColor(this.TintColor,
            (this._xamlRoot?.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default);
    }

    private static byte Blend(byte baseValue, byte tintValue, float tintAmount) =>
        (byte)Math.Round(baseValue + ((tintValue - baseValue) * tintAmount));
}
