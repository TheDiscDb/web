using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;

namespace TheDiscDb.UnitTests.Server;

public class WasmConfigMiddlewareTests
{
    [Test]
    public async Task InvokeAsync_ConfiguredLicense_ReturnsOnlyPublicConfiguration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Syncfusion:LicenseKey"] = "test-license",
            ["GraphQL:ApiKeyAuthentication:Enabled"] = "true",
            ["GraphQL:ApiKeyAuthentication:PublicApiKey"] = "public-key",
            ["GraphQL:ApiKeyAuthentication:AdminApiKey"] = "private-key",
            ["Syncfusion:PrivateSetting"] = "not-for-the-browser"
        }).Build();
        var middleware = new WasmConfigMiddleware(_ => Task.CompletedTask, configuration, new TestEnvironment());
        var context = CreateContext("/appsettings.json");

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        var syncfusion = json.RootElement.GetProperty("Syncfusion");
        var auth = json.RootElement.GetProperty("GraphQL").GetProperty("ApiKeyAuthentication");
        await Assert.That(syncfusion.GetProperty("LicenseKey").GetString()).IsEqualTo("test-license");
        await Assert.That(syncfusion.TryGetProperty("PrivateSetting", out _)).IsFalse();
        await Assert.That(auth.GetProperty("PublicApiKey").GetString()).IsEqualTo("public-key");
        await Assert.That(auth.TryGetProperty("AdminApiKey", out _)).IsFalse();
        await Assert.That(json.RootElement.TryGetProperty("Contributions", out _)).IsTrue();
    }

    [Test]
    public async Task InvokeAsync_MissingLicense_DoesNotAddLicenseSection()
    {
        var configuration = new ConfigurationBuilder().Build();
        var middleware = new WasmConfigMiddleware(_ => Task.CompletedTask, configuration, new TestEnvironment());
        var context = CreateContext("/appsettings.json");

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        await Assert.That(json.RootElement.TryGetProperty("Syncfusion", out _)).IsFalse();
    }

    [Test]
    public async Task InvokeAsync_OtherPath_PassesThrough()
    {
        var wasCalled = false;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Syncfusion:LicenseKey"] = "test-license"
        }).Build();
        var middleware = new WasmConfigMiddleware(
            _ => { wasCalled = true; return Task.CompletedTask; }, configuration, new TestEnvironment());

        await middleware.InvokeAsync(CreateContext("/"));

        await Assert.That(wasCalled).IsTrue();
    }

    private static DefaultHttpContext CreateContext(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = nameof(WasmConfigMiddlewareTests);
        public string EnvironmentName { get; set; } = "Development";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}