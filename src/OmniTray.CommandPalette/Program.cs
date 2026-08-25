// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.CommandPalette;

public static class Program
{
    [MTAThread]
    public static async Task Main(string[] args)
    {
        var host = ExtensionHostConfiguration.Resolve(
            args,
            new ExtensionHostRunnerParameters
            {
                PublisherMoniker = ExtensionHostIdentity.PublisherMoniker,
                ProductMoniker = ExtensionHostIdentity.ProductMoniker
            });

        await ExtensionHostRunner.CreateBuilder(host)
            .AddHostedExtensionFactory(context => new OmniTrayExtension(context.ExtensionDisposedEvent))
            .RunAsync();
    }
}
