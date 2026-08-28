// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace OmniTray.ViewModels;

public sealed partial class StackOrganizerScopeViewModel(EdgeShelfSide? side, string displayName) : ObservableObject
{
    public EdgeShelfSide? Side { get; } = side;
    public string DisplayName { get; } = displayName;

    [ObservableProperty]
    public partial string StatusText { get; private set; } = string.Empty;

    internal void UpdateStatus(string statusText) => this.StatusText = statusText;
}
