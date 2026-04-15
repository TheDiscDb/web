using Microsoft.AspNetCore.Http;

namespace TheDiscDb.UnitTests.Server;

public class LowercaseUrlMiddlewareTests
{
    [Test]
    public async Task InvokeAsync_LowercasePath_PassesThrough()
    {
        var wasCalled = false;
        RequestDelegate next = _ => { wasCalled = true; return Task.CompletedTask; };
        var middleware = new LowercaseUrlMiddleware(next);
        var context = CreateHttpContext("GET", "/movies/the-matrix");

        await middleware.InvokeAsync(context);

        await Assert.That(wasCalled).IsTrue();
    }

    [Test]
    public async Task InvokeAsync_UppercasePath_Returns301Redirect()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = new LowercaseUrlMiddleware(next);
        var context = CreateHttpContext("GET", "/Movies/The-Matrix");

        await middleware.InvokeAsync(context);

        await Assert.That(context.Response.StatusCode).IsEqualTo(301);
        await Assert.That(context.Response.Headers.Location.ToString()).IsEqualTo("http://localhost/movies/the-matrix");
    }

    [Test]
    public async Task InvokeAsync_ContributionPath_SkipsRedirect()
    {
        var wasCalled = false;
        RequestDelegate next = _ => { wasCalled = true; return Task.CompletedTask; };
        var middleware = new LowercaseUrlMiddleware(next);
        var context = CreateHttpContext("GET", "/contribution/AbCdEf");

        await middleware.InvokeAsync(context);

        await Assert.That(wasCalled).IsTrue();
        await Assert.That(context.Response.StatusCode).IsNotEqualTo(301);
    }

    [Test]
    public async Task InvokeAsync_AdminContributionPath_SkipsRedirect()
    {
        var wasCalled = false;
        RequestDelegate next = _ => { wasCalled = true; return Task.CompletedTask; };
        var middleware = new LowercaseUrlMiddleware(next);
        var context = CreateHttpContext("GET", "/admin/contribution/AbCdEf");

        await middleware.InvokeAsync(context);

        await Assert.That(wasCalled).IsTrue();
    }

    [Test]
    public async Task InvokeAsync_ApiPath_SkipsRedirect()
    {
        var wasCalled = false;
        RequestDelegate next = _ => { wasCalled = true; return Task.CompletedTask; };
        var middleware = new LowercaseUrlMiddleware(next);
        var context = CreateHttpContext("GET", "/api/SomeThing");

        await middleware.InvokeAsync(context);

        await Assert.That(wasCalled).IsTrue();
    }

    [Test]
    public async Task InvokeAsync_GraphqlPath_SkipsRedirect()
    {
        var wasCalled = false;
        RequestDelegate next = _ => { wasCalled = true; return Task.CompletedTask; };
        var middleware = new LowercaseUrlMiddleware(next);
        var context = CreateHttpContext("GET", "/graphql?Query=test");

        await middleware.InvokeAsync(context);

        await Assert.That(wasCalled).IsTrue();
    }

    [Test]
    public async Task InvokeAsync_PostRequest_DoesNotRedirect()
    {
        var wasCalled = false;
        RequestDelegate next = _ => { wasCalled = true; return Task.CompletedTask; };
        var middleware = new LowercaseUrlMiddleware(next);
        var context = CreateHttpContext("POST", "/Movies/Create");

        await middleware.InvokeAsync(context);

        await Assert.That(wasCalled).IsTrue();
    }

    [Test]
    public async Task InvokeAsync_Redirect_PreservesQueryString()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = new LowercaseUrlMiddleware(next);
        var context = CreateHttpContext("GET", "/Movies/Search", "?q=Matrix&page=1");

        await middleware.InvokeAsync(context);

        await Assert.That(context.Response.Headers.Location.ToString()).IsEqualTo("http://localhost/movies/search?q=Matrix&page=1");
    }

    [Test]
    public async Task InvokeAsync_EmptyPath_PassesThrough()
    {
        var wasCalled = false;
        RequestDelegate next = _ => { wasCalled = true; return Task.CompletedTask; };
        var middleware = new LowercaseUrlMiddleware(next);
        var context = CreateHttpContext("GET", "");

        await middleware.InvokeAsync(context);

        await Assert.That(wasCalled).IsTrue();
    }

    // ====================================================================
    // Regression: Sqids encoded IDs in disc log upload URLs
    //
    // The disc upload page gives users a command (PowerShell/bash) to POST
    // disc logs to: /api/contribute/{sqidContributionId}/discs/{sqidDiscId}/logs
    //
    // Sqids IDs are case-sensitive (e.g. "AbCdEf" != "abcdef"). If the
    // middleware lowercases these URLs, IdEncoder.Decode() returns a wrong ID
    // or throws, causing a 500 error during disc log upload.
    //
    // The /contribution/* client-side routes have the same problem: the user
    // navigates to /contribution/{sqid}/discs/{sqid}/identify after upload.
    // ====================================================================

    [Test]
    public async Task Regression_DiscLogUploadUrl_PreservesSqidCase()
    {
        // Exact URL pattern generated by DiscUpload.razor.cs GetUri():
        //   {baseUri}api/contribute/{ContributionId}/discs/{DiscId}/logs
        const string originalPath = "/api/contribute/AbCdEf/discs/XyZaBc/logs";
        var wasCalled = false;
        RequestDelegate next = _ => { wasCalled = true; return Task.CompletedTask; };
        var middleware = new LowercaseUrlMiddleware(next);
        var context = CreateHttpContext("POST", originalPath);

        await middleware.InvokeAsync(context);

        await Assert.That(wasCalled).IsTrue();
        await Assert.That(context.Request.Path.Value).IsEqualTo(originalPath);
        await Assert.That(context.Response.StatusCode is < 300 or > 399).IsTrue();
    }

    [Test]
    public async Task Regression_DiscLogUploadUrl_GET_PreservesSqidCase()
    {
        // Even a GET to the API path must not be lowercased — the /api/ prefix
        // protects all methods, not just POST
        const string originalPath = "/api/contribute/AbCdEf/discs/XyZaBc/logs";
        var wasCalled = false;
        RequestDelegate next = _ => { wasCalled = true; return Task.CompletedTask; };
        var middleware = new LowercaseUrlMiddleware(next);
        var context = CreateHttpContext("GET", originalPath);

        await middleware.InvokeAsync(context);

        await Assert.That(wasCalled).IsTrue();
        await Assert.That(context.Request.Path.Value).IsEqualTo(originalPath);
        await Assert.That(context.Response.StatusCode is < 300 or > 399).IsTrue();
    }

    [Test]
    public async Task Regression_ContributionDiscIdentifyUrl_PreservesSqidCase()
    {
        // After upload, DiscUpload.razor.cs redirects to:
        //   /contribution/{ContributionId}/discs/{DiscId}/identify
        const string originalPath = "/contribution/AbCdEf/discs/XyZaBc/identify";
        var wasCalled = false;
        RequestDelegate next = _ => { wasCalled = true; return Task.CompletedTask; };
        var middleware = new LowercaseUrlMiddleware(next);
        var context = CreateHttpContext("GET", originalPath);

        await middleware.InvokeAsync(context);

        await Assert.That(wasCalled).IsTrue();
        await Assert.That(context.Request.Path.Value).IsEqualTo(originalPath);
        await Assert.That(context.Response.StatusCode is < 300 or > 399).IsTrue();
    }

    [Test]
    public async Task Regression_ContributionDetailUrl_PreservesSqidCase()
    {
        // BreadCrumbHelper generates: /contribution/{encodedId}
        const string originalPath = "/contribution/K9mRtL";
        var wasCalled = false;
        RequestDelegate next = _ => { wasCalled = true; return Task.CompletedTask; };
        var middleware = new LowercaseUrlMiddleware(next);
        var context = CreateHttpContext("GET", originalPath);

        await middleware.InvokeAsync(context);

        await Assert.That(wasCalled).IsTrue();
        await Assert.That(context.Request.Path.Value).IsEqualTo(originalPath);
        await Assert.That(context.Response.StatusCode is < 300 or > 399).IsTrue();
    }

    [Test]
    public async Task Regression_AdminContributionDiscUrl_PreservesSqidCase()
    {
        // Admin routes also use encoded IDs:
        //   /admin/contribution/{id}/discs/{discId}
        const string originalPath = "/admin/contribution/AbCdEf/discs/XyZaBc";
        var wasCalled = false;
        RequestDelegate next = _ => { wasCalled = true; return Task.CompletedTask; };
        var middleware = new LowercaseUrlMiddleware(next);
        var context = CreateHttpContext("GET", originalPath);

        await middleware.InvokeAsync(context);

        await Assert.That(wasCalled).IsTrue();
        await Assert.That(context.Request.Path.Value).IsEqualTo(originalPath);
        await Assert.That(context.Response.StatusCode is < 300 or > 399).IsTrue();
    }

    [Test]
    public async Task Regression_ApiExternalSearch_PreservesSqidCase()
    {
        // /api/contribute/externalsearch/{type} also goes through the API prefix
        const string originalPath = "/api/contribute/externalsearch/Movie";
        var wasCalled = false;
        RequestDelegate next = _ => { wasCalled = true; return Task.CompletedTask; };
        var middleware = new LowercaseUrlMiddleware(next);
        var context = CreateHttpContext("GET", originalPath);

        await middleware.InvokeAsync(context);

        await Assert.That(wasCalled).IsTrue();
        await Assert.That(context.Request.Path.Value).IsEqualTo(originalPath);
        await Assert.That(context.Response.StatusCode is < 300 or > 399).IsTrue();
    }

    [Test]
    public async Task Regression_NonSqidMoviePath_StillLowercased()
    {
        // Normal content paths without Sqids should STILL redirect
        // e.g. /Movie/The-Matrix -> /movie/the-matrix
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = new LowercaseUrlMiddleware(next);
        var context = CreateHttpContext("GET", "/Movie/The-Matrix");

        await middleware.InvokeAsync(context);

        await Assert.That(context.Response.StatusCode).IsEqualTo(301);
        await Assert.That(context.Response.Headers.Location.ToString()).IsEqualTo("http://localhost/movie/the-matrix");
    }

    [Test]
    public async Task Regression_BoxsetPath_StillLowercased()
    {
        // /Boxset/LOTR -> /boxset/lotr (no Sqids here)
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = new LowercaseUrlMiddleware(next);
        var context = CreateHttpContext("GET", "/Boxset/LOTR-Collection");

        await middleware.InvokeAsync(context);

        await Assert.That(context.Response.StatusCode).IsEqualTo(301);
        await Assert.That(context.Response.Headers.Location.ToString()).IsEqualTo("http://localhost/boxset/lotr-collection");
    }

    // ====================================================================
    // Regression: Static file extensions embedded in page URLs
    //
    // When a cached page still has relative asset references (before the
    // absolute-path fix is deployed), the browser resolves them against
    // the current page URL. This produces requests like:
    //   /series/.../discs/s01d01/00000/thediscdb.xyz.styles.css
    // which match the TitleDetail Blazor route and return a full HTML 404
    // page instead of a clean 404 status.
    // ====================================================================

    [Test]
    public async Task Regression_EmbeddedCssRequest_Returns404()
    {
        var wasCalled = false;
        RequestDelegate next = _ => { wasCalled = true; return Task.CompletedTask; };
        var middleware = new LowercaseUrlMiddleware(next);
        var context = CreateHttpContext("GET",
            "/series/band-of-brothers-2001/releases/2015-band-of-brothers-blu-ray/discs/s01d01/00000/thediscdb.0hudxurhxl.styles.css");

        await middleware.InvokeAsync(context);

        await Assert.That(wasCalled).IsFalse();
        await Assert.That(context.Response.StatusCode).IsEqualTo(404);
    }

    [Test]
    public async Task Regression_EmbeddedJsRequest_Returns404()
    {
        var wasCalled = false;
        RequestDelegate next = _ => { wasCalled = true; return Task.CompletedTask; };
        var middleware = new LowercaseUrlMiddleware(next);
        var context = CreateHttpContext("GET",
            "/movie/the-matrix/releases/some-release/discs/disc-1/blazor.web.js");

        await middleware.InvokeAsync(context);

        await Assert.That(wasCalled).IsFalse();
        await Assert.That(context.Response.StatusCode).IsEqualTo(404);
    }

    [Test]
    public async Task Regression_RootLevelStaticFile_PassesThrough()
    {
        // Root-level static files like /favicon.ico should NOT be blocked
        var wasCalled = false;
        RequestDelegate next = _ => { wasCalled = true; return Task.CompletedTask; };
        var middleware = new LowercaseUrlMiddleware(next);
        var context = CreateHttpContext("GET", "/favicon.ico");

        await middleware.InvokeAsync(context);

        await Assert.That(wasCalled).IsTrue();
    }

    [Test]
    public async Task Regression_EmbeddedFontRequest_Returns404()
    {
        var wasCalled = false;
        RequestDelegate next = _ => { wasCalled = true; return Task.CompletedTask; };
        var middleware = new LowercaseUrlMiddleware(next);
        var context = CreateHttpContext("GET",
            "/series/something/releases/r1/discs/d1/fonts/somefont.woff2");

        await middleware.InvokeAsync(context);

        await Assert.That(wasCalled).IsFalse();
        await Assert.That(context.Response.StatusCode).IsEqualTo(404);
    }

    [Test]
    public async Task Regression_StaleWasmRequest_Returns404()
    {
        var wasCalled = false;
        RequestDelegate next = _ => { wasCalled = true; return Task.CompletedTask; };
        var middleware = new LowercaseUrlMiddleware(next);
        var context = CreateHttpContext("GET",
            "/_framework/System.Runtime.Serialization.qfpmfujegm.wasm");

        await middleware.InvokeAsync(context);

        // /_framework is a case-sensitive prefix, so the static file guard skips it
        // and the request passes through to be handled by MapStaticAssets
        await Assert.That(wasCalled).IsTrue();
    }

    [Test]
    public async Task Regression_WasmInPageUrl_Returns404()
    {
        var wasCalled = false;
        RequestDelegate next = _ => { wasCalled = true; return Task.CompletedTask; };
        var middleware = new LowercaseUrlMiddleware(next);
        var context = CreateHttpContext("GET",
            "/movie/the-matrix/releases/some-release/discs/disc-1/dotnet.native.abc123.wasm");

        await middleware.InvokeAsync(context);

        await Assert.That(wasCalled).IsFalse();
        await Assert.That(context.Response.StatusCode).IsEqualTo(404);
    }

    [Test]
    public async Task Regression_FrameworkJs_PassesThrough()
    {
        var wasCalled = false;
        RequestDelegate next = _ => { wasCalled = true; return Task.CompletedTask; };
        var middleware = new LowercaseUrlMiddleware(next);
        var context = CreateHttpContext("GET", "/_framework/blazor.web.js");

        await middleware.InvokeAsync(context);

        await Assert.That(wasCalled).IsTrue();
    }

    [Test]
    public async Task Regression_ContentCss_PassesThrough()
    {
        var wasCalled = false;
        RequestDelegate next = _ => { wasCalled = true; return Task.CompletedTask; };
        var middleware = new LowercaseUrlMiddleware(next);
        var context = CreateHttpContext("GET", "/_content/Syncfusion.Blazor.Themes/bootstrap5.css");

        await middleware.InvokeAsync(context);

        await Assert.That(wasCalled).IsTrue();
    }

    private static DefaultHttpContext CreateHttpContext(string method, string path, string queryString = "")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(string.IsNullOrEmpty(queryString) ? "" : queryString);
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost");
        return context;
    }
}
