// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.Text.Json;
using Windows.Storage;

namespace OmniTray.Services;

internal sealed class StackRepository
{
    private const string CatalogFileName = "stack-catalog.json";
    private const string TemporaryCatalogFileName = "stack-catalog.tmp";
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<StackCatalogState> LoadAsync()
    {
        await this._gate.WaitAsync();
        try
        {
            var folder = ApplicationData.Current.LocalFolder;
            var item = await folder.TryGetItemAsync(CatalogFileName);
            if (item is not StorageFile file)
            {
                return StackCatalogState.Empty;
            }

            try
            {
                var json = await FileIO.ReadTextAsync(file);
                var catalog = JsonSerializer.Deserialize(
                    json,
                    StackCatalogJsonContext.Default.StackCatalogDocument);
                if (catalog is null)
                {
                    throw new JsonException("The stack catalog was empty.");
                }

                return StackCatalogJson.Restore(catalog);
            }
            catch
            {
                await PreserveCorruptCatalogAsync(file);
                return StackCatalogState.Empty;
            }
        }
        finally
        {
            this._gate.Release();
        }
    }

    public async Task SaveAsync(StackCatalogState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var catalog = StackCatalogJson.CreateDocument(state);
        var json = JsonSerializer.Serialize(
            catalog,
            StackCatalogJsonContext.Default.StackCatalogDocument);

        await this._gate.WaitAsync();
        try
        {
            var folder = ApplicationData.Current.LocalFolder;
            var temporaryFile = await folder.CreateFileAsync(
                TemporaryCatalogFileName,
                CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(temporaryFile, json);

            var existing = await folder.TryGetItemAsync(CatalogFileName);
            if (existing is StorageFile existingFile)
            {
                await temporaryFile.MoveAndReplaceAsync(existingFile);
            }
            else
            {
                await temporaryFile.RenameAsync(
                    CatalogFileName,
                    NameCollisionOption.ReplaceExisting);
            }
        }
        finally
        {
            this._gate.Release();
        }
    }

    private static async Task PreserveCorruptCatalogAsync(StorageFile file)
    {
        try
        {
            await file.RenameAsync(
                $"stack-catalog.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.json",
                NameCollisionOption.GenerateUniqueName);
        }
        catch
        {
            // Recovery still succeeds with an empty catalogue if quarantine fails.
        }
    }
}
