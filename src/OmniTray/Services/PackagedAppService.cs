// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Runtime.InteropServices;

namespace OmniTray.Services;

internal sealed record PackagedAppDescriptor(string DisplayName, string AppUserModelId);

internal static unsafe partial class PackagedAppService
{
    private const int EApplicationNotRegistered = unchecked((int)0x80270254);
    private const int RpcEChangedMode = unchecked((int)0x80010106);
    private const uint ClsctxLocalServer = 0x4;

    private static readonly Guid IidShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
    private static readonly Guid IidShellItem2 = new("7E9FB0D3-919F-4307-AB2E-9B1860310C93");
    private static readonly Guid IidEnumShellItems = new("70629033-E363-4A28-A567-0DB78006E6D7");
    private static readonly Guid BhidEnumItems = new("94F60519-2850-4924-AA5A-D15E84868039");
    private static readonly Guid ClsidApplicationActivationManager =
        new("45BA127D-10A8-46EA-8AB7-56EA9078943C");
    private static readonly Guid IidApplicationActivationManager =
        new("2E941141-7F97-4756-BA1D-9DECDE894A3D");
    private static readonly PropertyKey AppUserModelIdKey =
        new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

    public static Task<IReadOnlyList<PackagedAppDescriptor>> GetInstalledAppsAsync() =>
        Task.Run<IReadOnlyList<PackagedAppDescriptor>>(GetInstalledApps);

    public static Task ActivateFilesAsync(string appUserModelId, IReadOnlyList<string> paths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appUserModelId);
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0)
        {
            throw new ArgumentException("At least one file is required.", nameof(paths));
        }

        var pathSnapshot = paths.Select(Path.GetFullPath).ToArray();
        return Task.Run(() => ActivateFiles(appUserModelId.Trim(), pathSnapshot));
    }

    private static IReadOnlyList<PackagedAppDescriptor> GetInstalledApps()
    {
        var shouldUninitialize = InitializeCom();
        nint appsFolder = 0;
        nint enumerator = 0;
        try
        {
            var shellItemId = IidShellItem;
            ThrowIfFailed(
                SHCreateItemFromParsingName("shell:AppsFolder", 0, in shellItemId, out appsFolder),
                "Opening the installed-apps folder");

            var enumHandlerId = BhidEnumItems;
            var enumShellItemsId = IidEnumShellItems;
            var bindToHandler = (delegate* unmanaged[Stdcall]<nint, nint, Guid*, Guid*, nint*, int>)
                GetVtableEntry(appsFolder, 3);
            ThrowIfFailed(
                bindToHandler(
                    appsFolder,
                    0,
                    &enumHandlerId,
                    &enumShellItemsId,
                    &enumerator),
                "Enumerating installed apps");

            var apps = new Dictionary<string, PackagedAppDescriptor>(StringComparer.OrdinalIgnoreCase);
            while (true)
            {
                nint item = 0;
                uint fetched = 0;
                var next = (delegate* unmanaged[Stdcall]<nint, uint, nint*, uint*, int>)
                    GetVtableEntry(enumerator, 3);
                var result = next(enumerator, 1, &item, &fetched);
                if (result < 0)
                {
                    Release(item);
                    ThrowIfFailed(result, "Reading an installed app");
                }

                if (fetched == 0 || item == 0)
                {
                    break;
                }

                try
                {
                    var appUserModelId = GetShellItemString(item, AppUserModelIdKey);
                    if (string.IsNullOrWhiteSpace(appUserModelId) || !appUserModelId.Contains('!'))
                    {
                        continue;
                    }

                    var displayName = GetDisplayName(item);
                    apps[appUserModelId] = new PackagedAppDescriptor(
                        string.IsNullOrWhiteSpace(displayName) ? appUserModelId : displayName,
                        appUserModelId);
                }
                finally
                {
                    Release(item);
                }
            }

            return apps.Values
                .OrderBy(static app => app.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(static app => app.AppUserModelId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            Release(enumerator);
            Release(appsFolder);
            if (shouldUninitialize)
            {
                CoUninitialize();
            }
        }
    }

    private static void ActivateFiles(string appUserModelId, IReadOnlyList<string> paths)
    {
        var shouldUninitialize = InitializeCom();
        var itemIds = new nint[paths.Count];
        nint itemArray = 0;
        nint activationManager = 0;
        try
        {
            for (var index = 0; index < paths.Count; index++)
            {
                ThrowIfFailed(
                    SHParseDisplayName(paths[index], 0, out itemIds[index], 0, 0),
                    $"Opening {Path.GetFileName(paths[index])}");
            }

            fixed (nint* itemIdsPointer = itemIds)
            {
                ThrowIfFailed(
                    SHCreateShellItemArrayFromIDLists((uint)itemIds.Length, (nint)itemIdsPointer, out itemArray),
                    "Preparing files for activation");
            }

            var activationManagerClassId = ClsidApplicationActivationManager;
            var activationManagerInterfaceId = IidApplicationActivationManager;
            ThrowIfFailed(
                CoCreateInstance(
                    in activationManagerClassId,
                    0,
                    ClsctxLocalServer,
                    in activationManagerInterfaceId,
                    out activationManager),
                "Creating the application activation manager");

            var activateForFile =
                (delegate* unmanaged[Stdcall]<nint, char*, nint, char*, uint*, int>)
                GetVtableEntry(activationManager, 4);
            uint processId = 0;
            fixed (char* appUserModelIdPointer = appUserModelId)
            {
                var result = activateForFile(
                    activationManager,
                    appUserModelIdPointer,
                    itemArray,
                    null,
                    &processId);
                if (result == EApplicationNotRegistered)
                {
                    // Full-trust packaged apps such as Paint do not implement the UWP file contract.
                    // Their manifest file associations pass paths as launch arguments instead.
                    var activateApplication =
                        (delegate* unmanaged[Stdcall]<nint, char*, char*, uint, uint*, int>)
                        GetVtableEntry(activationManager, 3);
                    var arguments = CreateLaunchArguments(paths);
                    fixed (char* argumentsPointer = arguments)
                    {
                        ThrowIfFailed(
                            activateApplication(
                                activationManager,
                                appUserModelIdPointer,
                                argumentsPointer,
                                0,
                                &processId),
                            "Activating the packaged desktop application");
                    }
                }
                else
                {
                    ThrowIfFailed(result, "Activating the packaged application");
                }
            }
        }
        finally
        {
            Release(activationManager);
            Release(itemArray);
            foreach (var itemId in itemIds)
            {
                if (itemId != 0)
                {
                    CoTaskMemFree(itemId);
                }
            }

            if (shouldUninitialize)
            {
                CoUninitialize();
            }
        }
    }

    private static string CreateLaunchArguments(IReadOnlyList<string> paths) =>
        string.Join(' ', paths.Select(static path => $"\"{path}\""));

    private static string? GetShellItemString(nint item, PropertyKey propertyKey)
    {
        var shellItem2Id = IidShellItem2;
        nint shellItem2 = 0;
        var queryInterface = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)
            GetVtableEntry(item, 0);
        var queryResult = queryInterface(item, &shellItem2Id, &shellItem2);
        if (queryResult < 0)
        {
            Release(shellItem2);
            return null;
        }

        if (shellItem2 == 0)
        {
            return null;
        }

        try
        {
            nint valuePointer = 0;
            var getString = (delegate* unmanaged[Stdcall]<nint, PropertyKey*, nint*, int>)
                GetVtableEntry(shellItem2, 17);
            var result = getString(shellItem2, &propertyKey, &valuePointer);
            if (valuePointer == 0)
            {
                return null;
            }

            try
            {
                return result < 0 ? null : Marshal.PtrToStringUni(valuePointer);
            }
            finally
            {
                CoTaskMemFree(valuePointer);
            }
        }
        finally
        {
            Release(shellItem2);
        }
    }

    private static string? GetDisplayName(nint item)
    {
        nint valuePointer = 0;
        var getDisplayName = (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)
            GetVtableEntry(item, 5);
        var result = getDisplayName(item, 0, &valuePointer);
        if (valuePointer == 0)
        {
            return null;
        }

        try
        {
            return result < 0 ? null : Marshal.PtrToStringUni(valuePointer);
        }
        finally
        {
            CoTaskMemFree(valuePointer);
        }
    }

    private static bool InitializeCom()
    {
        var result = CoInitializeEx(0, 0);
        if (result == RpcEChangedMode)
        {
            return false;
        }

        ThrowIfFailed(result, "Initializing COM");
        return true;
    }

    private static nint GetVtableEntry(nint instance, int slot) => (*(nint**)instance)[slot];

    private static void Release(nint instance)
    {
        if (instance == 0)
        {
            return;
        }

        var release = (delegate* unmanaged[Stdcall]<nint, uint>)GetVtableEntry(instance, 2);
        _ = release(instance);
    }

    private static void ThrowIfFailed(int result, string operation)
    {
        if (result < 0)
        {
            throw new COMException($"{operation} failed (0x{result:X8}).", result);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct PropertyKey(Guid FormatId, uint PropertyId);

    [LibraryImport("shell32.dll", EntryPoint = "SHCreateItemFromParsingName", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHCreateItemFromParsingName(
        string parsingName,
        nint bindContext,
        in Guid interfaceId,
        out nint shellItem);

    [LibraryImport("shell32.dll", EntryPoint = "SHParseDisplayName", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHParseDisplayName(
        string name,
        nint bindContext,
        out nint itemId,
        uint attributesIn,
        nint attributesOut);

    [LibraryImport("shell32.dll")]
    private static partial int SHCreateShellItemArrayFromIDLists(
        uint itemCount,
        nint itemIds,
        out nint shellItemArray);

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, uint concurrencyModel);

    [LibraryImport("ole32.dll")]
    private static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    private static partial void CoTaskMemFree(nint memory);

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(
        in Guid classId,
        nint outer,
        uint context,
        in Guid interfaceId,
        out nint instance);
}
