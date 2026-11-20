using Microsoft.Net.Http.Headers;

namespace Ardea.Host.Http;

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

    internal static bool IsHashed(string fileName) =>
        System.Text.RegularExpressions.Regex.IsMatch(fileName, @"-[A-Z0-9]{8,}\.(js|css|woff2?|svg|png)$");
}
