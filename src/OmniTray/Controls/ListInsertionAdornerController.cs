// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using Windows.Foundation;
using Microsoft.UI.Xaml.Media;

namespace OmniTray.Controls;

internal readonly record struct ListInsertionTarget(
    int InsertionIndex,
    int ContainerIndex,
    InsertionPlacement Placement);

internal sealed class ListInsertionAdornerController
{
    private readonly string _adornerName;
    private readonly ListViewBase _list;
    private InsertionAdorner? _activeAdorner;
    private Orientation _orientation;
    private bool _wraps;

    public ListInsertionAdornerController(
        ListViewBase list,
        string adornerName,
        Orientation orientation)
    {
        this._list = list;
        this._adornerName = adornerName;
        this._orientation = orientation;
    }

    public void SetLayout(Orientation orientation, bool wraps = false)
    {
        this.Clear();
        this._orientation = orientation;
        this._wraps = wraps;
    }

    public ListInsertionTarget? Resolve(Point position)
    {
        if (this._list.Items.Count == 0)
        {
            // An empty collection has a valid insertion point, but no tile to host an adorner.
            return new ListInsertionTarget(0, -1, InsertionPlacement.Before);
        }

        var realized = Enumerable.Range(0, this._list.Items.Count)
            .Select(index => (Index: index, Container: this._list.ContainerFromIndex(index) as FrameworkElement))
            .Where(static value => value.Container is not null)
            .Select(value => (
                value.Index,
                Container: value.Container!,
                Bounds: value.Container!.TransformToVisual(this._list).TransformBounds(
                    new Rect(0, 0, value.Container.ActualWidth, value.Container.ActualHeight))))
            .Where(static value => value.Bounds.Width > 0 && value.Bounds.Height > 0)
            .ToArray();
        if (realized.Length == 0)
        {
            return null;
        }

        if (this._wraps)
        {
            var rowAnchor = realized.MinBy(value =>
                Math.Abs((value.Bounds.Y + (value.Bounds.Height / 2)) - position.Y));
            var rowTolerance = Math.Max(1, rowAnchor.Bounds.Height / 2);
            var row = realized
                .Where(value => Math.Abs(
                    (value.Bounds.Y + (value.Bounds.Height / 2)) -
                    (rowAnchor.Bounds.Y + (rowAnchor.Bounds.Height / 2))) < rowTolerance)
                .OrderBy(static value => value.Bounds.X)
                .ToArray();
            foreach (var value in row)
            {
                if (position.X < value.Bounds.X + (value.Bounds.Width / 2))
                {
                    return new ListInsertionTarget(
                        value.Index,
                        value.Index,
                        InsertionPlacement.Before);
                }
            }

            var lastInRow = row[^1];
            return new ListInsertionTarget(
                lastInRow.Index + 1,
                lastInRow.Index,
                InsertionPlacement.After);
        }

        var pointerAxis = this._orientation == Orientation.Vertical ? position.Y : position.X;
        foreach (var value in realized)
        {
            var start = this._orientation == Orientation.Vertical ? value.Bounds.Y : value.Bounds.X;
            var length = this._orientation == Orientation.Vertical ? value.Bounds.Height : value.Bounds.Width;
            if (pointerAxis < start + (length / 2))
            {
                return new ListInsertionTarget(
                    value.Index,
                    value.Index,
                    InsertionPlacement.Before);
            }
        }

        var last = realized[^1];
        return new ListInsertionTarget(
            last.Index + 1,
            last.Index,
            InsertionPlacement.After);
    }

    public void Show(ListInsertionTarget target)
    {
        var adorner = this.FindAdorner(target.ContainerIndex);
        if (!ReferenceEquals(this._activeAdorner, adorner))
        {
            this._activeAdorner?.Hide();
            this._activeAdorner = adorner;
        }

        this._activeAdorner?.Show(this._orientation, target.Placement);
    }

    public void Clear()
    {
        this._activeAdorner?.Hide();
        this._activeAdorner = null;
    }

    private InsertionAdorner? FindAdorner(int containerIndex) =>
        containerIndex >= 0 && this._list.ContainerFromIndex(containerIndex) is DependencyObject container
            ? this.FindDescendant(container)
            : null;

    private InsertionAdorner? FindDescendant(DependencyObject parent)
    {
        if (parent is InsertionAdorner { Name: var name } adorner && name == this._adornerName)
        {
            return adorner;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            if (this.FindDescendant(VisualTreeHelper.GetChild(parent, index)) is { } result)
            {
                return result;
            }
        }

        return null;
    }
}
