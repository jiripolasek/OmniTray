// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace OmniTray.Services;

internal static class DropCommandDialogService
{
    public static Task<bool> ConfirmExecutionAsync(
        Window owner,
        DropCommandConfirmationRequest request)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(request);

        return StackDialogWindow.ShowAsync(
            owner,
            request.Title,
            request.Message,
            request.PrimaryButtonText);
    }
}
