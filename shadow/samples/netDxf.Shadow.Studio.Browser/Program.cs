using System.Text;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using netDxf.Shadow.Studio.Browser;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<RuntimeStatus>("#blazor-runtime");
using var http = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
BrowserRuntime.Initialize(await BrowserApplication.CreateAsync(http));
await builder.Build().RunAsync();
