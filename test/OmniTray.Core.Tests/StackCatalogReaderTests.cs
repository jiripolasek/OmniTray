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
    public void ReadStacks_RestoresRichTextAndUrlMetadata()
    {
        var stackId = Guid.NewGuid();
        var richTextId = Guid.NewGuid();
        var urlId = Guid.NewGuid();
        var json = $$"""
                     {
                       "stacks": [
                         {
                           "id": "{{stackId}}",
                           "name": "Reusable content",
                           "tint": "Neutral",
                           "items": [
                             {
                               "id": "{{richTextId}}",
                               "kind": 2,
                               "displayName": "Region Sales",
                               "text": "Region\tSales",
                               "html": "<table><tr><td>Region</td><td>Sales</td></tr></table>",
                               "rtf": "{\\rtf1 Region\\tab Sales}",
                               "sourceApplicationName": "Microsoft Excel",
                               "applicationLink": "mailto:analyst@example.com",
                               "customFormats": [
                                 {
                                   "formatId": "Biff12",
                                   "kind": 1,
                                   "data": "AQIDBA=="
                                 },
                                 {
                                   "formatId": "Csv",
                                   "kind": 0,
                                   "text": "Region,Sales"
                                 }
                               ],
                               "isOwned": false,
                               "createdAt": "2026-08-26T08:00:00Z"
                             },
                             {
                               "id": "{{urlId}}",
                               "kind": 4,
                               "displayName": "Example",
                               "text": "https://example.com/",
                               "url": "https://example.com/",
                               "sourceUrl": "https://example.com/",
                               "isOwned": false,
                               "createdAt": "2026-08-26T08:01:00Z"
                             }
                           ]
                         }
                       ]
                     }
                     """;

        var stack = StackCatalogReader.ReadStacks(json).Single();

        Assert.AreEqual("Microsoft Excel", stack.Items[0].SourceApplicationName);
        Assert.AreEqual("mailto:analyst@example.com", stack.Items[0].ApplicationLink);
        Assert.IsNotNull(stack.Items[0].Html);
        Assert.IsNotNull(stack.Items[0].Rtf);
        Assert.HasCount(2, stack.Items[0].CustomFormats);
        Assert.AreEqual("Biff12", stack.Items[0].CustomFormats[0].FormatId);
        CollectionAssert.AreEqual(
            new byte[] { 1, 2, 3, 4 },
            stack.Items[0].CustomFormats[0].GetBinaryData());
        Assert.AreEqual("Region,Sales", stack.Items[0].CustomFormats[1].Text);
        Assert.AreEqual(DropItemKind.Uri, stack.Items[1].Kind);
        Assert.AreEqual("https://example.com/", stack.Items[1].Url);
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
