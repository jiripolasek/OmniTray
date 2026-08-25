// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace OmniTray.Services;

internal delegate ITrayWindowContent TrayWindowContentFactory(Window owner, bool isMinimal);

internal sealed record TrayContextAction(
    string Text,
    Symbol Icon,
    Action Execute,
    bool BeginsGroup = false);

internal interface ITrayWindowContent : IDisposable
{
    FrameworkElement View { get; }

    IReadOnlyList<TrayContextAction> ContextActions { get; }

    void PrepareForClose(Action completed);
}
