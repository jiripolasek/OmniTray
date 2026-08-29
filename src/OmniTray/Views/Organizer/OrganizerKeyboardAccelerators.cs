// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace OmniTray.Views.Organizer;

internal static class OrganizerKeyboardAccelerators
{
    internal static void ScopeTo(DependencyObject scopeOwner, params UIElement[] commandOwners)
    {
        ArgumentNullException.ThrowIfNull(scopeOwner);
        ArgumentNullException.ThrowIfNull(commandOwners);

        foreach (var commandOwner in commandOwners)
        {
            foreach (var accelerator in commandOwner.KeyboardAccelerators)
            {
                accelerator.ScopeOwner = scopeOwner;
            }
        }
    }
}
