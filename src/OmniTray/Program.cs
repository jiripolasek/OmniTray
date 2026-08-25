// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;
using WinRT;

namespace OmniTray;

internal static partial class Program
{
    private const uint CoWaitDefault = 0;
    private const uint InfiniteTimeout = 0xFFFFFFFF;
    private static readonly Lock ActivationSync = new();
    private static readonly Queue<AppActivationArguments> PendingActivations = new();
    private static App? _app;

    internal static AppActivationArguments InitialActivationArguments { get; private set; } = null!;

    [STAThread]
    private static int Main(string[] args)
    {
        ComWrappersSupport.InitializeComWrappers();

        InitialActivationArguments = AppInstance.GetCurrent().GetActivatedEventArgs();
        var keyInstance = AppInstance.FindOrRegisterForKey("OmniTray.SingleInstance");
        if (!keyInstance.IsCurrent)
        {
            RedirectActivationTo(InitialActivationArguments, keyInstance);
            return 0;
        }

        keyInstance.Activated += OnActivated;
        Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            var app = new App();
            AppActivationArguments[] pendingActivations;
            lock (ActivationSync)
            {
                _app = app;
                pendingActivations = PendingActivations.ToArray();
                PendingActivations.Clear();
            }

            foreach (var activation in pendingActivations)
            {
                app.HandleActivation(activation);
            }
        });
        return 0;
    }

    private static void RedirectActivationTo(
        AppActivationArguments activationArguments,
        AppInstance keyInstance)
    {
        using var redirectCompleted = new Semaphore(0, 1);
        _ = Task.Run(() =>
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                keyInstance.RedirectActivationToAsync(activationArguments)
                    .AsTask(cancellation.Token)
                    .GetAwaiter()
                    .GetResult();
            }
            catch
            {
                // The redirecting process has no UI; the primary instance remains unaffected.
            }
            finally
            {
                redirectCompleted.Release();
            }
        });

        nint[] handles = [redirectCompleted.SafeWaitHandle.DangerousGetHandle()];
        _ = CoWaitForMultipleObjects(
            CoWaitDefault,
            InfiniteTimeout,
            1,
            handles,
            out _);
    }

    private static void OnActivated(object? sender, AppActivationArguments args)
    {
        App? app;
        lock (ActivationSync)
        {
            app = _app;
            if (app is null)
            {
                PendingActivations.Enqueue(args);
                return;
            }
        }

        app.HandleActivation(args);
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoWaitForMultipleObjects(
        uint flags,
        uint timeout,
        int handleCount,
        nint[] handles,
        out uint index);
}
