// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.Text.Json;

namespace OmniTray.Core.Tests;

[TestClass]
public sealed class StackCatalogReaderTests
{
    [TestMethod]
    public void ReadStacks_RestoresSearchableStackContent()
    {
        var stackId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var createdAt = DateTimeOffset.Parse("2026-08-24T12:00:00Z");
        var json = $$"""
                     {
                       "stacks": [
                         {
                           "id": "{{stackId}}",
                           "name": "Research",
                           "tint": "Mint",
                           "items": [
                             {
                               "id": "{{itemId}}",
                               "kind": 2,
                               "displayName": "Palette notes",
                               "text": "Command Palette design",
                               "isOwned": true,
                               "createdAt": "{{createdAt:O}}"
                             }
                           ]
                         }
                       ],
                       "openTrayWindows": [],
                       "edgeShelves": []
                     }
                     """;

        var stacks = StackCatalogReader.ReadStacks(json);

        Assert.HasCount(1, stacks);
        Assert.AreEqual(stackId, stacks[0].Id);
        Assert.AreEqual("Research", stacks[0].Name);
        Assert.AreEqual(StackInspectorViewMode.List, stacks[0].InspectorViewMode);
        Assert.HasCount(1, stacks[0].Items);
        Assert.AreEqual(itemId, stacks[0].Items[0].Id);
        Assert.IsTrue(StackFilter.Matches(stacks[0], "palette design"));
    }

    [TestMethod]
    public void ReadStacks_RestoresPerStackInspectorViewMode()
    {
        var stackId = Guid.NewGuid();
        var json = $$"""
                     {
                       "stacks": [
                         {
                           "id": "{{stackId}}",
                           "name": "Images",
                           "tint": "Blue",
                           "inspectorViewMode": 1,
                           "items": []
                         }
                       ]
                     }
                     """;

        var stacks = StackCatalogReader.ReadStacks(json);

        Assert.HasCount(1, stacks);
        Assert.AreEqual(StackInspectorViewMode.Grid, stacks[0].InspectorViewMode);
    }

    [TestMethod]
    public void ReadStacks_RejectsDuplicateStackIds()
    {
        var stackId = Guid.NewGuid();
        var json = $$"""
                     {
                       "stacks": [
                         { "id": "{{stackId}}", "name": "One", "tint": "Blue", "items": [] },
                         { "id": "{{stackId}}", "name": "Two", "tint": "Mint", "items": [] }
                       ]
                     }
                     """;

        Assert.Throws<JsonException>(() => StackCatalogReader.ReadStacks(json));
    }
}
