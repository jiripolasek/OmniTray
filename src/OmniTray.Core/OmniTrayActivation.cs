// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core;

public enum OmniTrayActivationKind
{
    Open,
    Settings,
    NewStack,
    Stack,
    EdgeShelf
}

public sealed record OmniTrayActivationRequest(
    OmniTrayActivationKind Kind,
    Guid? StackId = null,
    string? Edge = null);

public static class OmniTrayActivation
{
    public const string Scheme = "omnitray";

    public static Uri OpenUri { get; } = new($"{Scheme}://open");

    public static Uri SettingsUri { get; } = new($"{Scheme}://settings");

    public static Uri NewStackUri { get; } = new($"{Scheme}://new-stack");

    public static Uri StackUri(Guid stackId)
    {
        if (stackId == Guid.Empty)
        {
            throw new ArgumentException("A stack ID is required.", nameof(stackId));
        }

        return new Uri($"{Scheme}://stack/{stackId:D}");
    }

    public static Uri EdgeShelfUri(string edge)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(edge);
        var normalizedEdge = edge.Trim().ToLowerInvariant();
        if (!IsKnownEdge(normalizedEdge))
        {
            throw new ArgumentOutOfRangeException(nameof(edge), edge, "Unknown OmniTray edge shelf.");
        }

        return new Uri($"{Scheme}://edge/{normalizedEdge}");
    }

    public static bool TryParse(Uri? uri, out OmniTrayActivationRequest? request)
    {
        request = null;
        if (uri is null ||
            !uri.IsAbsoluteUri ||
            !string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        var path = uri.AbsolutePath.Trim('/');
        switch (host)
        {
            case "open" when path.Length == 0:
                request = new OmniTrayActivationRequest(OmniTrayActivationKind.Open);
                return true;

            case "settings" when path.Length == 0:
                request = new OmniTrayActivationRequest(OmniTrayActivationKind.Settings);
                return true;

            case "new-stack" when path.Length == 0:
                request = new OmniTrayActivationRequest(OmniTrayActivationKind.NewStack);
                return true;

            case "stack" when Guid.TryParse(path, out var stackId) && stackId != Guid.Empty:
                request = new OmniTrayActivationRequest(OmniTrayActivationKind.Stack, stackId);
                return true;

            case "edge" when IsKnownEdge(path):
                request = new OmniTrayActivationRequest(OmniTrayActivationKind.EdgeShelf,
                    Edge: path.ToLowerInvariant());
                return true;

            default:
                return false;
        }
    }

    private static bool IsKnownEdge(string value) => value.Equals("left", StringComparison.OrdinalIgnoreCase) ||
                                                     value.Equals("right", StringComparison.OrdinalIgnoreCase) ||
                                                     value.Equals("top", StringComparison.OrdinalIgnoreCase) ||
                                                     value.Equals("bottom", StringComparison.OrdinalIgnoreCase);
}
