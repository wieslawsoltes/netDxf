using System.Text;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using netDxf.Shadow.Studio.Browser;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<RuntimeStatus>("#blazor-runtime");
var host = builder.Build();
var javascript = host.Services.GetRequiredService<IJSRuntime>();
try
{
    using var http = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
    BrowserRuntime.Initialize(await BrowserApplication.CreateAsync(http));
    // Notify JS from the loaded application, rather than invoking an assembly that
    // Blazor.start may not have loaded yet. Font initialization is included in this fence.
    await javascript.InvokeVoidAsync("shadowBrowserBoot.complete");
}
catch (Exception error)
{
    await javascript.InvokeVoidAsync("shadowBrowserBoot.fail", error.ToString());
    throw;
}
await host.RunAsync();
