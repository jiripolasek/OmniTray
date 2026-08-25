// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using Windows.ApplicationModel;

namespace OmniTray.Services;

internal sealed class StartupTaskService
{
    internal const string TaskId = "OmniTrayStartup";

    public async Task<StartupTaskState> GetStateAsync()
    {
        var startupTask = await StartupTask.GetAsync(TaskId);
        return startupTask.State;
    }

    public async Task<StartupTaskState> SetEnabledAsync(bool enabled)
    {
        var startupTask = await StartupTask.GetAsync(TaskId);
        if (enabled && startupTask.State == StartupTaskState.Disabled)
        {
            return await startupTask.RequestEnableAsync();
        }

        if (!enabled && startupTask.State == StartupTaskState.Enabled)
        {
            startupTask.Disable();
        }

        return startupTask.State;
    }
}
