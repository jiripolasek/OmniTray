// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace OmniTray.Controls;

internal static class AutoSuggestBoxGrouping
{
    public static void Enable(AutoSuggestBox searchBox, FrameworkElement? footer = null, FrameworkElement? emptyState = null)
    {
        searchBox.Loaded += (_, _) => UpdateSuggestionGrouping(searchBox, footer, emptyState);
        searchBox.RegisterPropertyChangedCallback(ItemsControl.ItemsSourceProperty, (_, _) => UpdateSuggestionGrouping(searchBox, footer, emptyState));
    }

    private static void UpdateSuggestionGrouping(AutoSuggestBox searchBox, FrameworkElement? footer, FrameworkElement? emptyState)
    {
        if (FindSuggestionsList(searchBox) is not { } list)
        {
            return;
        }

        // AutoSuggestBox's stock template does not forward GroupStyle to its native ListView.
        // Keep real collection groups so headers never become keyboard-selectable suggestions.
        if (!list.GroupStyle.SequenceEqual(searchBox.GroupStyle))
        {
            list.GroupStyle.Clear();
            foreach (var style in searchBox.GroupStyle)
            {
                list.GroupStyle.Add(style);
            }
        }

        list.ItemsSource = searchBox.ItemsSource;
        // Native AutoSuggestBox measures its ListView to place the popup, even when there are no items.
        list.Header = emptyState;

        // Keep the action outside the scrolling and selectable suggestions.
        if (footer is not null && list.Parent is Border container)
        {
            container.Child = null;
            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition());
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.Children.Add(list);
            Grid.SetRow(footer, 1);
            layout.Children.Add(footer);
            container.Child = layout;
        }
    }

    private static ListView? FindSuggestionsList(DependencyObject parent)
    {
        if (parent is ListView { Name: "SuggestionsList" } list)
        {
            return list;
        }

        if (parent is Popup { Child: { } popupChild })
        {
            return FindSuggestionsList(popupChild);
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            if (FindSuggestionsList(VisualTreeHelper.GetChild(parent, index)) is { } result)
            {
                return result;
            }
        }

        return null;
    }
}
