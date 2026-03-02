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
