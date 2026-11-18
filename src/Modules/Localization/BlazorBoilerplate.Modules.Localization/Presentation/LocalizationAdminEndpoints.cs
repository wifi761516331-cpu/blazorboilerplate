using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.Modules.Localization.Application;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorBoilerplate.Modules.Localization.Presentation;

public sealed class LocalizationAdminEndpoints(IServiceProvider rootServices)
{
    public void Map(IModuleEndpointBuilder builder)
    {
        // 语言管理
        builder.Map("/api/localization/admin/languages", "GET", "localization.language.read", async ctx =>
        {
            await using var scope = rootServices.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<LocalizationAdminService>();
            var list = await svc.GetAllLanguagesAsync(ctx.RequestAborted);
            await Results.Ok(list).ExecuteAsync(ctx);
        });

        builder.Map("/api/localization/admin/languages", "POST", "localization.language.write", async ctx =>
        {
            var req = await ctx.Request.ReadFromJsonAsync<CreateLanguageRequest>(ctx.RequestAborted);
            if (req is null)
            {
                await Results.BadRequest(new { message = "请求体不能为空。" }).ExecuteAsync(ctx);
                return;
            }

            try
            {
                await using var scope = rootServices.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<LocalizationAdminService>();
                var created = await svc.CreateLanguageAsync(req, ctx.RequestAborted);
                await Results.Ok(created).ExecuteAsync(ctx);
            }
            catch (InvalidOperationException ex)
            {
                await Results.BadRequest(new { message = ex.Message }).ExecuteAsync(ctx);
            }
        });

        builder.Map("/api/localization/admin/languages/{id:guid}", "PUT", "localization.language.write", async ctx =>
        {
            if (!Guid.TryParse(ctx.Request.RouteValues["id"]?.ToString(), out var id))
            {
                await Results.BadRequest(new { message = "无效的语言 ID。" }).ExecuteAsync(ctx);
                return;
            }

            var req = await ctx.Request.ReadFromJsonAsync<UpdateLanguageRequest>(ctx.RequestAborted);
            if (req is null)
            {
                await Results.BadRequest(new { message = "请求体不能为空。" }).ExecuteAsync(ctx);
                return;
            }

            try
            {
                await using var scope = rootServices.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<LocalizationAdminService>();
                var updated = await svc.UpdateLanguageAsync(id, req, ctx.RequestAborted);
                await Results.Ok(updated).ExecuteAsync(ctx);
            }
            catch (KeyNotFoundException ex)
            {
                await Results.NotFound(new { message = ex.Message }).ExecuteAsync(ctx);
            }
            catch (InvalidOperationException ex)
            {
                await Results.BadRequest(new { message = ex.Message }).ExecuteAsync(ctx);
            }
        });

        builder.Map("/api/localization/admin/languages/{id:guid}", "DELETE", "localization.language.write", async ctx =>
        {
            if (!Guid.TryParse(ctx.Request.RouteValues["id"]?.ToString(), out var id))
            {
                await Results.BadRequest(new { message = "无效的语言 ID。" }).ExecuteAsync(ctx);
                return;
            }

            try
            {
                await using var scope = rootServices.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<LocalizationAdminService>();
                await svc.DeleteLanguageAsync(id, ctx.RequestAborted);
                await Results.NoContent().ExecuteAsync(ctx);
            }
            catch (KeyNotFoundException ex)
            {
                await Results.NotFound(new { message = ex.Message }).ExecuteAsync(ctx);
            }
            catch (InvalidOperationException ex)
            {
                await Results.BadRequest(new { message = ex.Message }).ExecuteAsync(ctx);
            }
        });

        // 词条管理
        builder.Map("/api/localization/admin/modules", "GET", "localization.resource.read", async ctx =>
        {
            await using var scope = rootServices.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<LocalizationAdminService>();
            var modules = await svc.GetModulesAsync(ctx.RequestAborted);
            await Results.Ok(modules).ExecuteAsync(ctx);
        });

        builder.Map("/api/localization/admin/resources", "GET", "localization.resource.read", async ctx =>
        {
            var q = ctx.Request.Query;
            var culture = q["culture"].FirstOrDefault() ?? "zh-CN";
            var moduleId = q["moduleId"].FirstOrDefault();
            var keyword = q["keyword"].FirstOrDefault();
            var onlyMissing = bool.TryParse(q["onlyMissing"].FirstOrDefault(), out var om) && om;
            var page = int.TryParse(q["page"].FirstOrDefault(), out var p) ? p : 1;
            var pageSize = int.TryParse(q["pageSize"].FirstOrDefault(), out var ps) ? ps : 50;

            await using var scope = rootServices.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<LocalizationAdminService>();
            var result = await svc.QueryResourcesAsync(culture, moduleId, keyword, onlyMissing, page, pageSize, ctx.RequestAborted);
            await Results.Ok(result).ExecuteAsync(ctx);
        });

        builder.Map("/api/localization/admin/resources", "PUT", "localization.resource.write", async ctx =>
        {
            var req = await ctx.Request.ReadFromJsonAsync<UpsertResourcesRequest>(ctx.RequestAborted);
            if (req is null)
            {
                await Results.BadRequest(new { message = "请求体不能为空。" }).ExecuteAsync(ctx);
                return;
            }

            try
            {
                await using var scope = rootServices.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<LocalizationAdminService>();
                await svc.UpsertResourcesAsync(req, ctx.RequestAborted);
                await Results.NoContent().ExecuteAsync(ctx);
            }
            catch (InvalidOperationException ex)
            {
                await Results.BadRequest(new { message = ex.Message }).ExecuteAsync(ctx);
            }
        });

        builder.Map("/api/localization/admin/resources/reset", "POST", "localization.resource.write", async ctx =>
        {
            var req = await ctx.Request.ReadFromJsonAsync<ResetResourceRequest>(ctx.RequestAborted);
            if (req is null)
            {
                await Results.BadRequest(new { message = "请求体不能为空。" }).ExecuteAsync(ctx);
                return;
            }

            await using var scope = rootServices.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<LocalizationAdminService>();
            await svc.ResetResourceAsync(req, ctx.RequestAborted);
            await Results.NoContent().ExecuteAsync(ctx);
        });

        builder.Map("/api/localization/admin/resources/sync", "POST", "localization.resource.write", async ctx =>
        {
            await using var scope = rootServices.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<LocalizationAdminService>();
            var count = await svc.SyncFromRegistryAsync(ctx.RequestAborted);
            await Results.Ok(new { syncedCount = count }).ExecuteAsync(ctx);
        });

        builder.Map("/api/localization/admin/resources/export", "GET", "localization.resource.read", async ctx =>
        {
            var culture = ctx.Request.Query["culture"].FirstOrDefault() ?? "zh-CN";
            var moduleId = ctx.Request.Query["moduleId"].FirstOrDefault();

            await using var scope = rootServices.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<LocalizationAdminService>();
            var dict = await svc.ExportAsync(culture, moduleId, ctx.RequestAborted);
            await Results.Ok(dict).ExecuteAsync(ctx);
        });

        builder.Map("/api/localization/admin/resources/import", "POST", "localization.resource.write", async ctx =>
        {
            var req = await ctx.Request.ReadFromJsonAsync<ImportResourcesRequest>(ctx.RequestAborted);
            if (req is null)
            {
                await Results.BadRequest(new { message = "请求体不能为空。" }).ExecuteAsync(ctx);
                return;
            }

            await using var scope = rootServices.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<LocalizationAdminService>();
            var count = await svc.ImportAsync(req, ctx.RequestAborted);
            await Results.Ok(new { importedCount = count }).ExecuteAsync(ctx);
        });
    }
}

