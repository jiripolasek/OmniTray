// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.ComponentModel;
using Windows.UI;
using Microsoft.UI.Xaml.Media;

namespace OmniTray.ViewModels;

internal abstract class TrayContentViewModel : ObservableObject, IDisposable
{
    public abstract string Name { get; }

    public abstract string CompactName { get; }

    public abstract string AccessibleName { get; }

    public abstract string Tint { get; }

    public abstract Color TintColor { get; }

    public abstract SolidColorBrush TintBrush { get; }

    public abstract SolidColorBrush TintForegroundBrush { get; }

    public abstract void ChangeTint(string tint);

    public abstract void Dispose();
}

internal sealed class StackTrayContentViewModel : TrayContentViewModel
{
    public StackTrayContentViewModel(DropStackViewModel stack)
    {
        this.Stack = stack ?? throw new ArgumentNullException(nameof(stack));
        this.Stack.PropertyChanged += this.OnStackPropertyChanged;
    }

    internal DropStackViewModel Stack { get; }

    public override string Name => this.Stack.Name;

    public override string CompactName => this.Stack.CompactName;

    public override string AccessibleName => this.Stack.AccessibleName;

    public override string Tint => this.Stack.Tint;

    public override Color TintColor => this.Stack.TintColor;

    public override SolidColorBrush TintBrush => this.Stack.TintBrush;

    public override SolidColorBrush TintForegroundBrush => this.Stack.TintForegroundBrush;

    public override void ChangeTint(string tint) => this.Stack.ChangeTint(tint);

    public override void Dispose() => this.Stack.PropertyChanged -= this.OnStackPropertyChanged;

    private void OnStackPropertyChanged(object? sender, PropertyChangedEventArgs args) =>
        this.OnPropertyChanged(args.PropertyName);
}

internal sealed class DropCommandTrayContentViewModel : TrayContentViewModel
{
    private readonly Action<DropCommandInstance> _updateCommand;

    public DropCommandTrayContentViewModel(
        DropCommandViewModel command,
        Action<DropCommandInstance> updateCommand)
    {
        this.Command = command ?? throw new ArgumentNullException(nameof(command));
        this._updateCommand = updateCommand ?? throw new ArgumentNullException(nameof(updateCommand));
        this.Command.PropertyChanged += this.OnCommandPropertyChanged;
    }

    internal DropCommandViewModel Command { get; }

    public override string Name => this.Command.Name;

    public override string CompactName => this.Command.CompactName;

    public override string AccessibleName => this.Command.AccessibleName;

    public override string Tint => this.Command.Tint;

    public override Color TintColor => this.Command.TintColor;

    public override SolidColorBrush TintBrush => this.Command.TintBrush;

    public override SolidColorBrush TintForegroundBrush => this.Command.TintForegroundBrush;

    public override void ChangeTint(string tint) =>
        this._updateCommand(this.Command.Model.ChangeTint(tint));

    public override void Dispose() => this.Command.PropertyChanged -= this.OnCommandPropertyChanged;

    private void OnCommandPropertyChanged(object? sender, PropertyChangedEventArgs args) =>
        this.OnPropertyChanged(args.PropertyName);
}
