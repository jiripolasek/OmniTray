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
    private const string EdgeWindowsPausedKey = "EdgeWindowsPaused";
    private const string LeftEdgeWindowEnabledKey = "LeftEdgeWindowEnabled";
    private const string RightEdgeWindowEnabledKey = "RightEdgeWindowEnabled";
    private const string SyncAllEdgeContentKey = "SyncAllEdgeContent";
    private const string SyncLeftAndRightEdgeContentKey = "SyncLeftAndRightEdgeContent";
    private const string SyncTopAndBottomEdgeContentKey = "SyncTopAndBottomEdgeContent";
    private const string TopEdgeWindowEnabledKey = "TopEdgeWindowEnabled";
    private const string ToastPositionKey = "ToastPosition";
    private readonly ApplicationDataContainer _localSettings = ApplicationData.Current.LocalSettings;

    public bool AllowMoveOnDragOut
    {
        get =>
            this._localSettings.Values.TryGetValue(AllowMoveOnDragOutKey, out var value) &&
            value is bool enabled &&
            enabled;
        set => this._localSettings.Values[AllowMoveOnDragOutKey] = value;
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

    private bool GetBoolean(string key, bool defaultValue) =>
        this._localSettings.Values.TryGetValue(key, out var value) && value is bool enabled
            ? enabled
            : defaultValue;
}
