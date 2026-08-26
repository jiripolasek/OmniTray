// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core.Tests;

[TestClass]
public sealed class StackOperationsTests
{
    [TestMethod]
    [DataRow(true, false, false, false, false)]
    [DataRow(true, true, false, false, true)]
    [DataRow(true, false, true, false, true)]
    [DataRow(true, false, false, true, true)]
    [DataRow(false, true, true, true, false)]
    public void GameModePolicy_RequiresEnabledBlockingState(
        bool isEnabled,
        bool isRunningD3DFullScreen,
        bool isPresentationMode,
        bool isBusy,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            GameModePolicy.ShouldSuppressEdgeWindows(
                isEnabled,
                isRunningD3DFullScreen,
                isPresentationMode,
                isBusy));
    }

    [TestMethod]
    public void Combine_PreservesStackAndItemOrder()
    {
        var first = DropItem.CreateText("first");
        var second = DropItem.CreateText("second");
        var third = DropItem.CreateText("third");

        var combined = StackOperations.Combine(
        [
            DropStack.Create([first, second]),
            DropStack.Create([third])
        ]);

        CollectionAssert.AreEqual(
            new[] { first.Id, second.Id, third.Id },
            combined.Items.Select(static item => item.Id).ToArray());
    }

    [TestMethod]
    public void Split_PreservesRelativeOrderInBothResults()
    {
        var first = DropItem.CreateText("first");
        var second = DropItem.CreateText("second");
        var third = DropItem.CreateText("third");
        var source = DropStack.Create(
            [first, second, third],
            inspectorViewMode: StackInspectorViewMode.Grid);

        var (remaining, extracted) = StackOperations.Split(source, [second.Id]);

        CollectionAssert.AreEqual(
            new[] { first.Id, third.Id },
            remaining.Items.Select(static item => item.Id).ToArray());
        CollectionAssert.AreEqual(
            new[] { second.Id },
            extracted.Items.Select(static item => item.Id).ToArray());
        Assert.AreEqual(source.Id, remaining.Id);
        Assert.AreEqual(source.Name, remaining.Name);
        Assert.AreEqual(source.Tint, extracted.Tint);
        Assert.AreEqual(StackInspectorViewMode.Grid, remaining.InspectorViewMode);
        Assert.AreEqual(StackInspectorViewMode.Grid, extracted.InspectorViewMode);
    }

    [TestMethod]
    public void CombineInto_PreservesTargetIdentityAndAppendsSourceOrder()
    {
        var first = DropItem.CreateText("first");
        var second = DropItem.CreateText("second");
        var third = DropItem.CreateText("third");
        var target = DropStack.Create([first], "Inbox", "Mint");
        var source = DropStack.Create([second, third], "Later", "Violet");

        var combined = StackOperations.CombineInto(target, [source]);

        Assert.AreEqual(target.Id, combined.Id);
        Assert.AreEqual("Inbox", combined.Name);
        Assert.AreEqual("Mint", combined.Tint);
        CollectionAssert.AreEqual(
            new[] { first.Id, second.Id, third.Id },
            combined.Items.Select(static item => item.Id).ToArray());
    }

    [TestMethod]
    public void CombineInto_AllowsAnEmptySourceStack()
    {
        var target = DropStack.Create([DropItem.CreateText("kept")], "Inbox");
        var emptySource = DropStack.CreateEmpty();

        var combined = StackOperations.CombineInto(target, [emptySource]);

        Assert.AreSame(target, combined);
    }

    [TestMethod]
    public void Split_RejectsSelectingEveryItem()
    {
        var item = DropItem.CreateText("only item");
        var source = DropStack.Create([item]);

        Assert.ThrowsExactly<ArgumentException>(() => StackOperations.Split(source, [item.Id]));
    }

    [TestMethod]
    public void CreateText_UsesACompactDisplayPreview()
    {
        var item = DropItem.CreateText(new string('a', 80));

        Assert.HasCount(48, item.DisplayName);
        Assert.EndsWith("…", item.DisplayName);
        Assert.AreEqual(DropItemKind.Text, item.Kind);
    }

    [TestMethod]
    public void CreateStack_OwnsItsIdentity()
    {
        var stack = DropStack.Create(
            [DropItem.CreateText("one stack")],
            "Reading",
            "Violet");

        Assert.AreEqual("Reading", stack.Name);
        Assert.AreEqual("Violet", stack.Tint);
        Assert.HasCount(1, stack.Items);
    }

    [TestMethod]
    public void CreateStack_DefaultsToNeutralTint()
    {
        var populated = DropStack.Create([DropItem.CreateText("neutral color")]);
        var empty = DropStack.CreateEmpty();

        Assert.AreEqual("Neutral", populated.Tint);
        Assert.AreEqual("Neutral", empty.Tint);
    }

    [TestMethod]
    public void StackChanges_PreservePerStackInspectorViewMode()
    {
        var first = DropItem.CreateText("first");
        var second = DropItem.CreateText("second");
        var source = DropStack.Create([first], inspectorViewMode: StackInspectorViewMode.Grid);

        var changed = source
            .Rename("Reading")
            .ChangeTint("Violet")
            .Append([second])
            .ReorderItems([second.Id, first.Id]);

        Assert.AreEqual(StackInspectorViewMode.Grid, changed.InspectorViewMode);
    }

    [TestMethod]
    public void ChangeInspectorViewMode_UpdatesOnlyTheViewPreference()
    {
        var source = DropStack.Create([DropItem.CreateText("first")], "Reading", "Mint");

        var changed = source.ChangeInspectorViewMode(StackInspectorViewMode.Grid);

        Assert.AreEqual(source.Id, changed.Id);
        Assert.AreEqual(source.Name, changed.Name);
        Assert.AreEqual(source.Tint, changed.Tint);
        Assert.AreSame(source.Items, changed.Items);
        Assert.AreEqual(StackInspectorViewMode.Grid, changed.InspectorViewMode);
    }

    [TestMethod]
    public void Append_PreservesStackIdentityAndAddsItemsInOrder()
    {
        var first = DropItem.CreateText("first");
        var second = DropItem.CreateText("second");
        var source = DropStack.Create([first], "Reading", "Violet");

        var appended = source.Append([second]);

        Assert.AreEqual(source.Id, appended.Id);
        Assert.AreEqual("Reading", appended.Name);
        Assert.AreEqual("Violet", appended.Tint);
        CollectionAssert.AreEqual(
            new[] { first.Id, second.Id },
            appended.Items.Select(static item => item.Id).ToArray());
    }

    [TestMethod]
    public void Append_RejectsAnEmptyPayload()
    {
        var source = DropStack.Create([DropItem.CreateText("first")]);

        Assert.ThrowsExactly<ArgumentException>(() => source.Append([]));
    }

    [TestMethod]
    public void EmptyStack_CanReceiveItsFirstItemWithoutChangingIdentity()
    {
        var source = DropStack.CreateEmpty("Inbox", "Mint");
        var item = DropItem.CreateText("first");

        var appended = source.Append([item]);

        Assert.AreEqual(source.Id, appended.Id);
        Assert.AreEqual("Inbox", appended.Name);
        Assert.AreEqual("Mint", appended.Tint);
        CollectionAssert.AreEqual(new[] { item.Id }, appended.Items.Select(static value => value.Id).ToArray());
    }

    [TestMethod]
    public void DropImportDeduplication_SkipsAnExistingOriginalFileSystemPath()
    {
        var existing = DropItem.CreateImage("Lake.jpg", @"C:\Wallpapers\Lake.jpg");
        var duplicate = DropItem.CreateImage("lake.jpg", @"c:\wallpapers\.\lake.jpg");

        var additions = DropImportDeduplication.FilterNewItems([existing], [duplicate]);

        Assert.IsEmpty(additions);
    }

    [TestMethod]
    public void DropImportDeduplication_SkipsRepeatedOriginalPathsWithinOneDrop()
    {
        var first = DropItem.CreateStorageItem("Photo.jpg", @"C:\Pictures\Photo.jpg", false);
        var duplicate = DropItem.CreateImage("Photo.jpg", @"C:\Pictures\Photo.jpg");

        var additions = DropImportDeduplication.FilterNewItems([], [first, duplicate]);

        CollectionAssert.AreEqual(new[] { first.Id }, additions.Select(static item => item.Id).ToArray());
    }

    [TestMethod]
    public void DropImportDeduplication_PreservesRepeatableContentAndExplicitDuplicates()
    {
        var text = DropItem.CreateText("repeatable text");
        var repeatedText = DropItem.CreateText("repeatable text");
        var original = DropItem.CreateImage("Lake.jpg", @"C:\Wallpapers\Lake.jpg");
        var explicitDuplicate = DropItem.CreateImage("Lake copy.jpg", @"C:\Wallpapers\Lake.jpg");

        var additions = DropImportDeduplication.FilterNewItems([text], [repeatedText]);
        var inserted = StackOperations.InsertItems(DropStack.Create([original]), [explicitDuplicate], 1);

        CollectionAssert.AreEqual(new[] { repeatedText.Id }, additions.Select(static item => item.Id).ToArray());
        CollectionAssert.AreEqual(
            new[] { original.Id, explicitDuplicate.Id },
            inserted.Items.Select(static item => item.Id).ToArray());
    }

    [TestMethod]
    public void DropImportDeduplication_DoesNotCollapseDistinctOwnedCaptures()
    {
        var first = DropItem.CreateImage("Captured image", @"C:\AppData\capture.png", true);
        var second = DropItem.CreateImage("Captured image", @"C:\AppData\capture.png", true);

        var additions = DropImportDeduplication.FilterNewItems([first], [second]);

        CollectionAssert.AreEqual(new[] { second.Id }, additions.Select(static item => item.Id).ToArray());
    }

    [TestMethod]
    public void StackIdentityChanges_PreserveItemsAndId()
    {
        var item = DropItem.CreateText("identity");
        var source = DropStack.Create([item], "Inbox", "Mint");

        var changed = source.Rename("Later").ChangeTint("Violet");

        Assert.AreEqual(source.Id, changed.Id);
        Assert.AreEqual("Later", changed.Name);
        Assert.AreEqual("Violet", changed.Tint);
        CollectionAssert.AreEqual(new[] { item.Id }, changed.Items.Select(static value => value.Id).ToArray());
    }

    [TestMethod]
    public void RemoveItems_AllowsAStackToBecomeEmpty()
    {
        var item = DropItem.CreateText("temporary");
        var source = DropStack.Create([item]);

        var emptied = source.RemoveItems([item.Id]);

        Assert.AreEqual(source.Id, emptied.Id);
        Assert.IsEmpty(emptied.Items);
    }

    [TestMethod]
    public void ReorderItems_PreservesIdentityAndUsesTheRequestedOrder()
    {
        var first = DropItem.CreateText("first");
        var second = DropItem.CreateText("second");
        var third = DropItem.CreateText("third");
        var source = DropStack.Create([first, second, third], "Inbox", "Mint");

        var reordered = source.ReorderItems([third.Id, first.Id, second.Id]);

        Assert.AreEqual(source.Id, reordered.Id);
        Assert.AreEqual(source.Name, reordered.Name);
        Assert.AreEqual(source.Tint, reordered.Tint);
        CollectionAssert.AreEqual(
            new[] { third.Id, first.Id, second.Id },
            reordered.Items.Select(static item => item.Id).ToArray());
    }

    [TestMethod]
    public void ReorderItems_RejectsAnIncompleteOrder()
    {
        var first = DropItem.CreateText("first");
        var second = DropItem.CreateText("second");
        var source = DropStack.Create([first, second]);

        Assert.ThrowsExactly<ArgumentException>(() => source.ReorderItems([second.Id]));
    }

    [TestMethod]
    public void MoveItemsWithin_InsertsTheSelectedBlockBeforeTheTargetGap()
    {
        var first = DropItem.CreateText("first");
        var second = DropItem.CreateText("second");
        var third = DropItem.CreateText("third");
        var fourth = DropItem.CreateText("fourth");
        var stack = DropStack.Create([first, second, third, fourth]);

        var reordered = StackOperations.MoveItemsWithin(
            stack,
            [first.Id, third.Id],
            4);

        CollectionAssert.AreEqual(
            new[] { second.Id, fourth.Id, first.Id, third.Id },
            reordered.Items.Select(static item => item.Id).ToArray());
        Assert.AreEqual(stack.Id, reordered.Id);
    }

    [TestMethod]
    public void MoveItemsWithin_PreservesSourceOrderRatherThanPayloadOrder()
    {
        var first = DropItem.CreateText("first");
        var second = DropItem.CreateText("second");
        var third = DropItem.CreateText("third");
        var stack = DropStack.Create([first, second, third]);

        var reordered = StackOperations.MoveItemsWithin(
            stack,
            [third.Id, second.Id],
            0);

        CollectionAssert.AreEqual(
            new[] { second.Id, third.Id, first.Id },
            reordered.Items.Select(static item => item.Id).ToArray());
    }

    [TestMethod]
    public void MoveItemsWithin_DroppingIntoEitherAdjacentGapDoesNotDuplicateTheItem()
    {
        var first = DropItem.CreateText("first");
        var second = DropItem.CreateText("second");
        var stack = DropStack.Create([first, second]);

        var beforeItsOwnGap = StackOperations.MoveItemsWithin(stack, [first.Id], 0);
        var afterItsOwnGap = StackOperations.MoveItemsWithin(stack, [first.Id], 1);

        CollectionAssert.AreEqual(
            new[] { first.Id, second.Id },
            beforeItsOwnGap.Items.Select(static item => item.Id).ToArray());
        CollectionAssert.AreEqual(
            new[] { first.Id, second.Id },
            afterItsOwnGap.Items.Select(static item => item.Id).ToArray());
        Assert.HasCount(2, beforeItsOwnGap.Items);
        Assert.HasCount(2, afterItsOwnGap.Items);
    }

    [TestMethod]
    public void MoveItemsWithin_DroppingASelectionIntoItsOwnBoundaryGapsIsANoOp()
    {
        var first = DropItem.CreateText("first");
        var second = DropItem.CreateText("second");
        var third = DropItem.CreateText("third");
        var fourth = DropItem.CreateText("fourth");
        var stack = DropStack.Create([first, second, third, fourth]);
        var selectedIds = new[] { second.Id, third.Id };
        var expectedIds = stack.Items.Select(static item => item.Id).ToArray();

        var beforeSelection = StackOperations.MoveItemsWithin(stack, selectedIds, 1);
        var afterSelection = StackOperations.MoveItemsWithin(stack, selectedIds, 3);

        CollectionAssert.AreEqual(
            expectedIds,
            beforeSelection.Items.Select(static item => item.Id).ToArray());
        CollectionAssert.AreEqual(
            expectedIds,
            afterSelection.Items.Select(static item => item.Id).ToArray());
    }

    [TestMethod]
    [DataRow(0, 0, 3, 0)]
    [DataRow(0, 1, 3, 0)]
    [DataRow(0, 3, 3, 2)]
    [DataRow(2, 0, 3, 0)]
    [DataRow(2, 3, 3, 2)]
    public void ResolveDestinationIndex_UsesInsertionGapSemantics(
        int sourceIndex,
        int insertionIndex,
        int itemCount,
        int expectedIndex)
    {
        Assert.AreEqual(
            expectedIndex,
            ReorderOperations.ResolveDestinationIndex(sourceIndex, insertionIndex, itemCount));
    }

    [TestMethod]
    public void MoveItemsBetweenStacks_PreservesBothStackIdentitiesAndItemOrder()
    {
        var first = DropItem.CreateText("first");
        var second = DropItem.CreateText("second");
        var third = DropItem.CreateText("third");
        var targetItem = DropItem.CreateText("target");
        var source = DropStack.Create([first, second, third], "Source");
        var target = DropStack.Create([targetItem], "Target");

        var result = StackOperations.MoveItems(
            source,
            target,
            [third.Id, first.Id],
            0);

        CollectionAssert.AreEqual(
            new[] { second.Id },
            result.Source.Items.Select(static item => item.Id).ToArray());
        CollectionAssert.AreEqual(
            new[] { first.Id, third.Id, targetItem.Id },
            result.Target.Items.Select(static item => item.Id).ToArray());
        Assert.AreEqual(source.Id, result.Source.Id);
        Assert.AreEqual(target.Id, result.Target.Id);
    }

    [TestMethod]
    public void MoveItemsBetweenStacks_AllowsTheSourceToBecomeEmpty()
    {
        var item = DropItem.CreateText("only item");
        var source = DropStack.Create([item]);
        var target = DropStack.CreateEmpty();

        var result = StackOperations.MoveItems(source, target, [item.Id], 0);

        Assert.IsEmpty(result.Source.Items);
        CollectionAssert.AreEqual(
            new[] { item.Id },
            result.Target.Items.Select(static value => value.Id).ToArray());
    }

    [TestMethod]
    public void InsertItems_PreservesInsertionOrderAndRejectsIdCollisions()
    {
        var existing = DropItem.CreateText("existing");
        var first = DropItem.CreateText("first");
        var second = DropItem.CreateText("second");
        var target = DropStack.Create([existing]);

        var inserted = StackOperations.InsertItems(target, [first, second], 0);

        CollectionAssert.AreEqual(
            new[] { first.Id, second.Id, existing.Id },
            inserted.Items.Select(static item => item.Id).ToArray());
        Assert.ThrowsExactly<ArgumentException>(() =>
            StackOperations.InsertItems(inserted, [first], 0));
    }

    [TestMethod]
    public void CreateText_CanDescribeAnOwnedMaterialization()
    {
        var item = DropItem.CreateText("captured text", @"C:\captures\text.txt", true);

        Assert.AreEqual(DropItemKind.Text, item.Kind);
        Assert.AreEqual(@"C:\captures\text.txt", item.SourcePath);
        Assert.IsTrue(item.IsOwned);
        Assert.AreEqual("captured text", item.Text);
    }

    [TestMethod]
    public void Restore_PreservesOwnedItemAndStackIdentity()
    {
        var itemId = Guid.NewGuid();
        var stackId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var item = DropItem.Restore(
            itemId,
            DropItemKind.Image,
            "Captured image",
            @"C:\captures\image.png",
            null,
            true,
            createdAt);

        var stack = DropStack.Restore(stackId, "Images", "Violet", [item]);

        Assert.AreEqual(stackId, stack.Id);
        Assert.AreEqual(itemId, stack.Items[0].Id);
        Assert.IsTrue(stack.Items[0].IsOwned);
        Assert.AreEqual(createdAt, stack.Items[0].CreatedAt);
    }

    [TestMethod]
    public void EdgeLayout_AssigningAStackMovesItBetweenWindows()
    {
        var stack = DropStack.Create([DropItem.CreateText("placed")]);
        var firstEdgeWindow = EdgeWindowLayout.Create("Left", [stack.Id]);
        var secondEdgeWindow = EdgeWindowLayout.Create("Right");

        var reassigned = EdgeLayout.Create([firstEdgeWindow, secondEdgeWindow])
            .AssignStack(stack.Id, secondEdgeWindow.Id);

        Assert.IsEmpty(reassigned.Windows[0].StackIds);
        CollectionAssert.AreEqual(new[] { stack.Id }, reassigned.Windows[1].StackIds.ToArray());
    }

    [TestMethod]
    public void EdgeLayout_RejectsAStackInMultipleWindows()
    {
        var stackId = Guid.NewGuid();
        var firstEdgeWindow = EdgeWindowLayout.Create("Left", [stackId]);
        var secondEdgeWindow = EdgeWindowLayout.Create("Right", [stackId]);

        Assert.ThrowsExactly<ArgumentException>(() =>
            EdgeLayout.Create([firstEdgeWindow, secondEdgeWindow]));
    }

    [TestMethod]
    public void EdgeWindowLayout_ReorderStacksPreservesWindowIdentity()
    {
        var firstStackId = Guid.NewGuid();
        var secondStackId = Guid.NewGuid();
        var edgeWindow = EdgeWindowLayout.Create("Right", [firstStackId, secondStackId]);

        var reordered = edgeWindow.ReorderStacks([secondStackId, firstStackId]);

        Assert.AreEqual(edgeWindow.Id, reordered.Id);
        CollectionAssert.AreEqual(
            new[] { secondStackId, firstStackId },
            reordered.StackIds.ToArray());
    }

    [TestMethod]
    public void EdgeWindowLayout_RejectsAnIncompleteStackOrder()
    {
        var firstStackId = Guid.NewGuid();
        var secondStackId = Guid.NewGuid();
        var edgeWindow = EdgeWindowLayout.Create("Right", [firstStackId, secondStackId]);

        Assert.ThrowsExactly<ArgumentException>(() => edgeWindow.ReorderStacks([secondStackId]));
    }
}
