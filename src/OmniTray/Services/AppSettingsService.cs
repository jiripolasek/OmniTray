// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using Windows.Storage;

namespace OmniTray.Services;

internal sealed class AppSettingsService
{
    private const string AllowMoveOnDragOutKey = "AllowMoveOnDragOut";
    private const string BottomEdgeWindowEnabledKey = "BottomEdgeWindowEnabled";
    private const string EdgeGameModeEnabledKey = "EdgeGameModeEnabled";
    private const string EdgeWindowDockedKeyPrefix = "EdgeWindowDocked";
    private const string EdgeWindowDockThicknessKeyPrefix = "EdgeWindowDockThickness";
    private const string EdgeWindowsPausedKey = "EdgeWindowsPaused";
    private const string LeftEdgeWindowEnabledKey = "LeftEdgeWindowEnabled";
    private const string OpenInspectorOnHoverKey = "OpenInspectorOnHover";
    private const string RightEdgeWindowEnabledKey = "RightEdgeWindowEnabled";
    private const string ShakeToCreateTrayKey = "ShakeToCreateTray";
    private const string HorizontalStackCardDisplayModeKey = "HorizontalStackCardDisplayMode";
    private const string SyncAllEdgeContentKey = "SyncAllEdgeContent";
    private const string SyncLeftAndRightEdgeContentKey = "SyncLeftAndRightEdgeContent";
    private const string SyncTopAndBottomEdgeContentKey = "SyncTopAndBottomEdgeContent";
    private const string TopEdgeWindowEnabledKey = "TopEdgeWindowEnabled";
    private const string ToastPositionKey = "ToastPosition";
    private const string UseSystemAccentForNeutralKey = "UseSystemAccentForNeutral";
    private const string VerticalStackCardDisplayModeKey = "VerticalStackCardDisplayMode";
    private readonly ApplicationDataContainer _localSettings = ApplicationData.Current.LocalSettings;

    public bool AllowMoveOnDragOut
    {
        get =>
            this._localSettings.Values.TryGetValue(AllowMoveOnDragOutKey, out var value) &&
            value is bool enabled &&
            enabled;
        set => this._localSettings.Values[AllowMoveOnDragOutKey] = value;
    }

    public bool OpenInspectorOnHover
    {
        get => this.GetBoolean(OpenInspectorOnHoverKey, false);
        set => this._localSettings.Values[OpenInspectorOnHoverKey] = value;
    }

    public bool ShakeToCreateTray
    {
        get => this.GetBoolean(ShakeToCreateTrayKey, false);
        set => this._localSettings.Values[ShakeToCreateTrayKey] = value;
    }

    public bool UseSystemAccentForNeutral
    {
        get => this.GetBoolean(UseSystemAccentForNeutralKey, false);
        set => this._localSettings.Values[UseSystemAccentForNeutralKey] = value;
    }

    public bool EdgeGameModeEnabled
    {
        get => this.GetBoolean(EdgeGameModeEnabledKey, true);
        set => this._localSettings.Values[EdgeGameModeEnabledKey] = value;
    }

    public bool EdgeWindowsPaused
    {
        get => this.GetBoolean(EdgeWindowsPausedKey, false);
        set => this._localSettings.Values[EdgeWindowsPausedKey] = value;
    }

    public bool LeftEdgeWindowEnabled
    {
        get => this.GetBoolean(LeftEdgeWindowEnabledKey, true);
        set => this._localSettings.Values[LeftEdgeWindowEnabledKey] = value;
    }

    public bool RightEdgeWindowEnabled
    {
        get => this.GetBoolean(RightEdgeWindowEnabledKey, true);
        set => this._localSettings.Values[RightEdgeWindowEnabledKey] = value;
    }

    public bool TopEdgeWindowEnabled
    {
        get => this.GetBoolean(TopEdgeWindowEnabledKey, true);
        set => this._localSettings.Values[TopEdgeWindowEnabledKey] = value;
    }

    public bool BottomEdgeWindowEnabled
    {
        get => this.GetBoolean(BottomEdgeWindowEnabledKey, true);
        set => this._localSettings.Values[BottomEdgeWindowEnabledKey] = value;
    }

    public StackCardDisplayMode VerticalStackCardDisplayMode
    {
        get => this.GetEnum(VerticalStackCardDisplayModeKey, StackCardDisplayMode.LargeList);
        set => this._localSettings.Values[VerticalStackCardDisplayModeKey] = (int)value;
    }

    public StackCardDisplayMode HorizontalStackCardDisplayMode
    {
        get => this.GetEnum(HorizontalStackCardDisplayModeKey, StackCardDisplayMode.LargeList);
        set => this._localSettings.Values[HorizontalStackCardDisplayModeKey] = (int)value;
    }

    public bool SyncLeftAndRightEdgeContent
    {
        get => this.GetBoolean(SyncLeftAndRightEdgeContentKey, false);
        set => this._localSettings.Values[SyncLeftAndRightEdgeContentKey] = value;
    }

    public bool SyncTopAndBottomEdgeContent
    {
        get => this.GetBoolean(SyncTopAndBottomEdgeContentKey, false);
        set => this._localSettings.Values[SyncTopAndBottomEdgeContentKey] = value;
    }

    public bool SyncAllEdgeContent
    {
        get => this.GetBoolean(SyncAllEdgeContentKey, false);
        set => this._localSettings.Values[SyncAllEdgeContentKey] = value;
    }

    public ToastPosition ToastPosition
    {
        get
        {
            if (this._localSettings.Values.TryGetValue(ToastPositionKey, out var value) &&
                value is int storedValue &&
                Enum.IsDefined(typeof(ToastPosition), storedValue))
            {
                return (ToastPosition)storedValue;
            }

            return ToastPosition.UseSystemSettings;
        }

        set => this._localSettings.Values[ToastPositionKey] = (int)value;
    }

    public EdgeWindowSizeMode GetEdgeWindowSizeMode(EdgeShelfSide side) =>
        this.GetEnum($"{side}EdgeWindowSizeMode", EdgeWindowSizeMode.Reasonable);

    public void SetEdgeWindowSizeMode(EdgeShelfSide side, EdgeWindowSizeMode value) =>
        this._localSettings.Values[$"{side}EdgeWindowSizeMode"] = (int)value;

    public EdgeWindowAlignment GetEdgeWindowAlignment(EdgeShelfSide side) =>
        this.GetEnum($"{side}EdgeWindowAlignment", EdgeWindowAlignment.Center);

    public void SetEdgeWindowAlignment(EdgeShelfSide side, EdgeWindowAlignment value) =>
        this._localSettings.Values[$"{side}EdgeWindowAlignment"] = (int)value;

    public bool GetEdgeWindowDocked(ulong displayId, EdgeShelfSide side) =>
        this.GetBoolean(GetEdgeWindowDockedKey(displayId, side), false);

    public void SetEdgeWindowDocked(ulong displayId, EdgeShelfSide side, bool docked)
    {
        var key = GetEdgeWindowDockedKey(displayId, side);
        if (docked)
        {
            this._localSettings.Values[key] = true;
        }
        else
        {
            this._localSettings.Values.Remove(key);
        }
    }

    public double? GetEdgeWindowDockThickness(ulong displayId, EdgeShelfSide side) =>
        this._localSettings.Values.TryGetValue(GetEdgeWindowDockThicknessKey(displayId, side), out var value) &&
        value is double thickness &&
        double.IsFinite(thickness) &&
        thickness > 0
            ? thickness
            : null;

    public void SetEdgeWindowDockThickness(ulong displayId, EdgeShelfSide side, double thickness)
    {
        if (!double.IsFinite(thickness) || thickness <= 0)
        {
            return;
        }

        this._localSettings.Values[GetEdgeWindowDockThicknessKey(displayId, side)] = thickness;
    }

    private bool GetBoolean(string key, bool defaultValue) =>
        this._localSettings.Values.TryGetValue(key, out var value) && value is bool enabled
            ? enabled
            : defaultValue;

    private static string GetEdgeWindowDockedKey(ulong displayId, EdgeShelfSide side) =>
        $"{EdgeWindowDockedKeyPrefix}.{displayId:X16}.{side}";

    private static string GetEdgeWindowDockThicknessKey(ulong displayId, EdgeShelfSide side) =>
        $"{EdgeWindowDockThicknessKeyPrefix}.{displayId:X16}.{side}";

    private TEnum GetEnum<TEnum>(string key, TEnum defaultValue)
        where TEnum : struct, Enum =>
        this._localSettings.Values.TryGetValue(key, out var value) &&
        value is int storedValue &&
        Enum.IsDefined(typeof(TEnum), storedValue)
            ? (TEnum)Enum.ToObject(typeof(TEnum), storedValue)
            : defaultValue;
}
