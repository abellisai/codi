namespace Qwerty.Codi.Api.Configuration;

/// <summary>Ospita i due bundle Angular dallo stesso wwwroot: alunni alla radice, admin in /admin/.</summary>
public static class CodiWebApplicationExtensions
{
    public static WebApplication UseCodiWeb(this WebApplication application)
    {
        if (Directory.Exists(application.Environment.WebRootPath))
        {
            application.UseStaticFiles();
        }

        // Il fallback SPA non deve trasformare una rotta API sconosciuta in un HTML con stato 200.
        application.MapFallback("/{*path:nonfile}", (HttpContext context) =>
        {
            var path = context.Request.Path;
            if ((!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
                || IsBackendPath(path))
            {
                return Results.NotFound();
            }

            var index = path.StartsWithSegments("/admin", StringComparison.OrdinalIgnoreCase)
                ? "admin/index.html"
                : "index.html";
            var file = application.Environment.WebRootFileProvider.GetFileInfo(index);

            // Senza il bundle admin non si serve quello alunni; lo sviluppo può avviare solo l'API.
            return file.Exists && file.PhysicalPath is not null
                ? Results.File(file.PhysicalPath, "text/html; charset=utf-8")
                : Results.NotFound();
        });

        return application;
    }

    private static bool IsBackendPath(PathString path) =>
        path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/auth", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/public", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/media-assets", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/admin/media-assets", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/hubs", StringComparison.OrdinalIgnoreCase);
}
