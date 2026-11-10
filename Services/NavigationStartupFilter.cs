using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Requests.Services;

public sealed class NavigationStartupFilter : IStartupFilter
{
    private readonly ILogger<NavigationStartupFilter> _logger;

    public NavigationStartupFilter(ILogger<NavigationStartupFilter> logger)
    {
        _logger = logger;
    }

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(InjectNavigationScriptAsync);
            next(app);
        };
    }

    private async Task InjectNavigationScriptAsync(HttpContext context, Func<Task> next)
    {
        if (!HttpMethods.IsGet(context.Request.Method)
            || !IsWebIndex(context.Request.Path.Value))
        {
            await next().ConfigureAwait(false);
            return;
        }

        // Response compression runs later in the pipeline; if the client advertises gzip/br
        // we would receive compressed bytes here and corrupt them by treating them as text.
        // index.html is tiny, so ask for it uncompressed. Conditional headers are removed so
        // the static file middleware always returns a full 200 body we can rewrite.
        context.Request.Headers.Remove("Accept-Encoding");
        context.Request.Headers.Remove("If-None-Match");
        context.Request.Headers.Remove("If-Modified-Since");

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await next().ConfigureAwait(false);
            context.Response.Body = originalBody;
            buffer.Position = 0;

            if (context.Response.StatusCode != StatusCodes.Status200OK
                || context.Response.Headers.ContainsKey("Content-Encoding")
                || !(context.Response.ContentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) ?? false))
            {
                await buffer.CopyToAsync(originalBody).ConfigureAwait(false);
                return;
            }

            using var reader = new StreamReader(buffer, Encoding.UTF8, leaveOpen: true);
            var html = await reader.ReadToEndAsync().ConfigureAwait(false);
            html = WebClientInjector.Inject(html, out _);
            if (!html.Contains("data-requests-plugin", StringComparison.Ordinal))
            {
                _logger.LogWarning("Requests: could not find </body> in the web client index; the header button was not injected.");
            }

            var bytes = Encoding.UTF8.GetBytes(html);
            context.Response.ContentType = "text/html;charset=utf-8";
            context.Response.ContentLength = bytes.Length;
            context.Response.Headers.Remove("ETag");
            context.Response.Headers.Remove("Last-Modified");
            // Make browsers re-fetch index.html so the injected tag can't go stale.
            context.Response.Headers.CacheControl = "no-cache";
            await originalBody.WriteAsync(bytes).ConfigureAwait(false);
        }
        catch
        {
            context.Response.Body = originalBody;
            throw;
        }
    }

    private static bool IsWebIndex(string? path)
    {
        return !string.IsNullOrEmpty(path)
            && (path.EndsWith("/web", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/web/", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/web/index.html", StringComparison.OrdinalIgnoreCase));
    }
}
