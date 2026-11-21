namespace BlazorBoilerplate.WebHost.Services;

/// <summary>动态模块前端资源与混合渲染策略端点（样式聚合、版本清单、带版本号 DLL、RenderMode 切换）。</summary>
public static class ModuleClientEndpoints
{
    public static IEndpointRouteBuilder MapModuleClientEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/_modules");

        group.MapGet("/styles.css", (ModuleAssemblyCatalog catalog) =>
            Results.Content(catalog.GetCombinedCss(), "text/css; charset=utf-8"))
            .AllowAnonymous();

        group.MapGet("/client/manifest.json", (ModuleAssemblyCatalog catalog) =>
            Results.Ok(catalog.GetClientManifest()))
            .AllowAnonymous();

        group.MapGet("/client/{moduleId}/{version}/{fileName}", (
            string moduleId, string version, string fileName, ModuleAssemblyCatalog catalog, HttpContext http) =>
        {
            if (!catalog.TryGetClientAssemblyFile(moduleId, version, fileName, out var path) || path is null)
                return Results.NotFound();

            http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            return Results.File(path, "application/octet-stream");
        }).AllowAnonymous();

        group.MapPost("/client/{moduleId}/render-mode", (
            string moduleId, string mode, ModuleAssemblyCatalog catalog) =>
        {
            if (!Enum.TryParse<ModuleRenderStrategy>(mode, ignoreCase: true, out var strategy))
                return Results.BadRequest(new { error = "无效的渲染策略，仅支持 Auto 或 Server。" });
            return catalog.SetRenderStrategy(moduleId, strategy)
                ? Results.Ok(new { moduleId, renderMode = strategy.ToString() })
                : Results.BadRequest(new { error = "模块不存在或不支持切换为 Auto。" });
        }).RequireAuthorization();

        return endpoints;
    }
}

