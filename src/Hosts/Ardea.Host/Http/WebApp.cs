using Microsoft.Net.Http.Headers;

namespace Ardea.Host.Http;

/// <summary>
/// The API also serves the web app (master plan 12.2): no separate web server, no CORS. Hashed
/// bundles are cached for a year, <c>index.html</c> never; client routes fall back to it, while an
/// unknown API path stays a 404 problem.
/// </summary>
internal static class WebApp
{
    public static void Use(WebApplication app)
    {
        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = context =>
            {
                var headers = context.Context.Response.Headers;
                headers[HeaderNames.CacheControl] = IsHashed(context.File.Name)
                    ? "public, max-age=31536000, immutable"
                    : "no-cache";
            },
        });
    }

    public static void Map(WebApplication app)
    {
        app.MapFallback("/api/{**path}", () => Platform.Http.ApiProblems.Problem("not_found", StatusCodes.Status404NotFound, "No such API endpoint."))
            .ExcludeFromDescription();
        if (app.Environment.WebRootFileProvider.GetFileInfo("index.html").Exists)
        {
            app.MapFallbackToFile("{*path:nonfile}", "index.html", new StaticFileOptions
            {
                OnPrepareResponse = context => context.Context.Response.Headers[HeaderNames.CacheControl] = "no-cache",
            });
        }
    }

    /// <summary>Angular's output names carry a content hash: <c>main-ABCD1234.js</c>, <c>chunk-XYZ.js</c>.</summary>
    internal static bool IsHashed(string fileName) =>
        System.Text.RegularExpressions.Regex.IsMatch(fileName, @"-[A-Z0-9]{8,}\.(js|css|woff2?|svg|png)$");
}
