// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core.Tests;

[TestClass]
public sealed class OmniTrayActivationTests
{
    [TestMethod]
    public void NoteActivationRoundTripsAndRejectsMissingIdentity()
    {
        var id = Guid.NewGuid();
        Assert.IsTrue(OmniTrayActivation.TryParse(OmniTrayActivation.NoteUri(id), out var request));
        Assert.AreEqual(OmniTrayActivationKind.Note, request!.Kind);
        Assert.AreEqual(id, request.NoteId);
        Assert.IsNull(request.StackId);
        Assert.Throws<ArgumentException>(() => OmniTrayActivation.NoteUri(Guid.Empty));
        Assert.IsFalse(OmniTrayActivation.TryParse(new Uri("omnitray://note/invalid"), out _));
        Assert.IsFalse(OmniTrayActivation.TryParse(new Uri("omnitray://note/00000000-0000-0000-0000-000000000000"), out _));
    }

    [TestMethod]
    public void StandardUris_RoundTrip()
    {
        AssertActivation(OmniTrayActivation.OpenUri, OmniTrayActivationKind.Open);
        AssertActivation(OmniTrayActivation.SettingsUri, OmniTrayActivationKind.Settings);
        AssertActivation(OmniTrayActivation.NewStackUri, OmniTrayActivationKind.NewStack);
    }

    [TestMethod]
    public void StackUri_RoundTripsStackId()
    {
        var stackId = Guid.NewGuid();

        Assert.IsTrue(OmniTrayActivation.TryParse(OmniTrayActivation.StackUri(stackId), out var request));
        Assert.IsNotNull(request);
        Assert.AreEqual(OmniTrayActivationKind.Stack, request.Kind);
        Assert.AreEqual(stackId, request.StackId);
    }

    [TestMethod]
    [DataRow("left")]
    [DataRow("right")]
    [DataRow("top")]
    [DataRow("bottom")]
    public void EdgeShelfUri_RoundTripsEdge(string edge)
    {
        Assert.IsTrue(OmniTrayActivation.TryParse(OmniTrayActivation.EdgeShelfUri(edge), out var request));
        Assert.IsNotNull(request);
        Assert.AreEqual(OmniTrayActivationKind.EdgeShelf, request.Kind);
        Assert.AreEqual(edge, request.Edge);
    }

    [TestMethod]
    [DataRow("https://stack/6633bca1-b97b-4890-a337-acf5d1ce1a57")]
    [DataRow("omnitray://stack/not-a-guid")]
    [DataRow("omnitray://edge/diagonal")]
    [DataRow("omnitray://open/unexpected")]
    public void TryParse_RejectsInvalidActivation(string uri)
    {
        Assert.IsFalse(OmniTrayActivation.TryParse(new Uri(uri), out _));
    }

    private static void AssertActivation(Uri uri, OmniTrayActivationKind expectedKind)
    {
        Assert.IsTrue(OmniTrayActivation.TryParse(uri, out var request));
        Assert.IsNotNull(request);
        Assert.AreEqual(expectedKind, request.Kind);
    }
}
