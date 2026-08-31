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
    public void ReadStacks_RoundTripsAllNotePlacementsAndEmptyRtf()
    {
        var note = StickyNote.Create("Formatted Ω", @"{\rtf1\b Formatted \u937?}", NoteColor.Peach);
        var attached = StickyNote.Create("Attached");
        var empty = StickyNote.Create();
        var stack = DropStack.Create([
            DropItem.CreateNote(note),
            DropItem.CreateText("Parent").WithAttachedNotes([attached])
        ]).Append([DropItem.CreateNote(empty)]);
        var json = JsonSerializer.Serialize(new { stacks = new[] { stack } },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var restored = StackCatalogReader.ReadStacks(json);
        foreach (var expected in NoteOperations.Enumerate([stack]))
        {
            Assert.AreEqual(expected, NoteOperations.Find(restored, expected.Note.Id));
        }

        Assert.AreEqual(note.Text, restored[0].Items[0].Text);
        Assert.AreEqual(note.Rtf, restored[0].Items[0].Rtf);
    }

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
        Assert.AreEqual(StackItemSortMode.Default, stacks[0].ItemSortMode);
        Assert.HasCount(1, stacks[0].Items);
        Assert.AreEqual(itemId, stacks[0].Items[0].Id);
        Assert.IsTrue(StackFilter.Matches(stacks[0], "palette design"));
    }

    [TestMethod]
    public void ReadStacks_RestoresPerStackViewPreferences()
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
                           "itemSortMode": 2,
                           "items": []
                         }
                       ]
                     }
                     """;

        var stacks = StackCatalogReader.ReadStacks(json);

        Assert.HasCount(1, stacks);
        Assert.AreEqual(StackInspectorViewMode.Grid, stacks[0].InspectorViewMode);
        Assert.AreEqual(StackItemSortMode.Newest, stacks[0].ItemSortMode);
    }

    [TestMethod]
    public void ReadStacks_RestoresVirtualSource()
    {
        var stackId = Guid.NewGuid();
        var json = $$"""
                     {
                       "stacks": [
                         {
                           "id": "{{stackId}}",
                           "name": "Recent files",
                           "tint": "Neutral",
                           "virtualSource": {
                             "providerId": "builtin.recent-files",
                             "capabilities": 1
                           },
                           "items": []
                         }
                       ]
                     }
                     """;

        var stack = StackCatalogReader.ReadStacks(json).Single();

        Assert.AreEqual("builtin.recent-files", stack.VirtualSource?.ProviderId);
        Assert.AreEqual(VirtualStackCapabilities.Read, stack.VirtualSource?.Capabilities);
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
    public void ReadStacks_RestoresCaptureProvenanceBackingFileFactsAndHtmlResources()
    {
        var stackId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        var json = $$"""
                     {
                       "stacks": [
                         {
                           "id": "{{stackId}}",
                           "name": "Captured",
                           "tint": "Blue",
                           "items": [
                             {
                               "id": "{{itemId}}",
                               "kind": 0,
                               "displayName": "Report.xlsx",
                               "sourcePath": "C:\\Reports\\Report.xlsx",
                               "sourceApplicationName": "Microsoft Excel",
                               "sourcePackageFamilyName": "Microsoft.Office.Excel_8wekyb3d8bbwe",
                               "sourceApplicationLink": "ms-excel://sheet/42",
                               "capture": {
                                 "captureId": "{{captureId}}",
                                 "channel": 1,
                                 "capturedAt": "2026-08-26T11:26:07Z",
                                 "ordinal": 2,
                                 "requestedOperation": 3,
                                 "formats": [
                                   { "formatId": "Biff12", "status": 1, "detail": "6671 bytes" },
                                   { "formatId": "Embed Source", "status": 2, "detail": "COMException" }
                                 ]
                               },
                               "backing": { "kind": 1, "path": "C:\\Reports\\Report.xlsx" },
                               "fileFacts": {
                                 "originalFileName": "Report.xlsx",
                                 "contentType": "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                                 "size": 4096,
                                 "modifiedAt": "2026-08-25T10:00:00Z"
                               },
                               "htmlResources": [
                                 {
                                   "resourceKey": "https://example.com/chart.png",
                                   "managedRelativePath": "Content\\html-resource.png",
                                   "size": 128
                                 }
                               ],
                               "isOwned": false,
                               "createdAt": "2026-08-26T11:26:07Z"
                             }
                           ]
                         }
                       ]
                     }
                     """;

        var item = StackCatalogReader.ReadStacks(json).Single().Items.Single();

        Assert.AreEqual("Microsoft.Office.Excel_8wekyb3d8bbwe", item.SourcePackageFamilyName);
        Assert.AreEqual("ms-excel://sheet/42", item.SourceApplicationLink);
        Assert.AreEqual(captureId, item.Capture?.CaptureId);
        Assert.AreEqual(CaptureChannel.Clipboard, item.Capture?.Channel);
        Assert.AreEqual(2, item.Capture?.Ordinal);
        Assert.AreEqual(CaptureRequestedOperation.Copy | CaptureRequestedOperation.Move,
            item.Capture?.RequestedOperation);
        Assert.AreEqual(DataFormatReadStatus.Failed, item.Capture?.Formats[1].Status);
        Assert.AreEqual(ContentBackingKind.OriginalPath, item.Backing.Kind);
        Assert.AreEqual((ulong)4096, item.FileFacts?.Size);
        Assert.AreEqual("https://example.com/chart.png", item.HtmlResources.Single().ResourceKey);
        Assert.IsTrue(StackFilter.Matches(DropStack.Create([item]), "from:excel"));
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
