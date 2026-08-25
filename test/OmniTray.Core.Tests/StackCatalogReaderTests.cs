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
                       "version": 5,
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
        Assert.HasCount(1, stacks[0].Items);
        Assert.AreEqual(itemId, stacks[0].Items[0].Id);
        Assert.IsTrue(StackFilter.Matches(stacks[0], "palette design"));
    }

    [TestMethod]
    public void ReadStacks_RejectsFutureCatalogVersion()
    {
        var json = $$"""{"version":{{StackCatalogReader.CurrentVersion + 1}},"stacks":[]}""";

        Assert.Throws<JsonException>(() => StackCatalogReader.ReadStacks(json));
    }

    [TestMethod]
    public void ReadStacks_RejectsDuplicateStackIds()
    {
        var stackId = Guid.NewGuid();
        var json = $$"""
                     {
                       "version": 5,
                       "stacks": [
                         { "id": "{{stackId}}", "name": "One", "tint": "Blue", "items": [] },
                         { "id": "{{stackId}}", "name": "Two", "tint": "Mint", "items": [] }
                       ]
                     }
                     """;

        Assert.Throws<JsonException>(() => StackCatalogReader.ReadStacks(json));
    }
}
