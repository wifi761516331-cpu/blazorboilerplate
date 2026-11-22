using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.ModuleAbstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorBoilerplate.Modules.Domains;

public sealed class DomainsEndpoints(IServiceProvider moduleServices)
{
    public void Map(IModuleEndpointBuilder builder)
    {
        builder.Map("/api/domains/resolve", "GET", "anonymous", async ctx => await WithScope(ctx, async s =>
        {
            var host = ctx.Request.Query["host"].FirstOrDefault() ?? ctx.Request.Host.Host;
            var result = await s.ServiceProvider.GetRequiredService<DomainsService>().ResolveAsync(host, ctx.RequestAborted);
            await ctx.Response.WriteAsJsonAsync(result, ctx.RequestAborted);
        }));

        builder.Map("/api/domains/bindings", "GET", "domains.route.read", async ctx => await WithScope(ctx, async s =>
        {
            var list = await s.ServiceProvider.GetRequiredService<DomainsService>().ListAsync(ctx.RequestAborted);
            await ctx.Response.WriteAsJsonAsync(list, ctx.RequestAborted);
        }));

        builder.Map("/api/domains/bindings", "POST", "domains.route.write", async ctx => await WithScope(ctx, async s =>
        {
            var req = await ReadJsonAsync<DomainRouteUpsertRequest>(ctx);
            var created = await s.ServiceProvider.GetRequiredService<DomainsService>().CreateAsync(req, ctx.RequestAborted);
            await ctx.Response.WriteAsJsonAsync(created, ctx.RequestAborted);
        }));

        builder.Map("/api/domains/bindings/{id}", "PUT", "domains.route.write", async ctx => await WithScope(ctx, async s =>
        {
            var req = await ReadJsonAsync<DomainRouteUpsertRequest>(ctx);
            var updated = await s.ServiceProvider.GetRequiredService<DomainsService>().UpdateAsync(GetId(ctx), req, ctx.RequestAborted);
            await ctx.Response.WriteAsJsonAsync(updated, ctx.RequestAborted);
        }));

        builder.Map("/api/domains/bindings/{id}", "DELETE", "domains.route.write", async ctx => await WithScope(ctx, async s =>
        {
            await s.ServiceProvider.GetRequiredService<DomainsService>().DeleteAsync(GetId(ctx), ctx.RequestAborted);
            ctx.Response.StatusCode = StatusCodes.Status204NoContent;
        }));
    }

    private async Task WithScope(HttpContext ctx, Func<IServiceScope, Task> handler)
    {
        try
        {
            using var scope = moduleServices.CreateScope();
            await handler(scope);
        }
        catch (KeyNotFoundException ex)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            await ctx.Response.WriteAsJsonAsync(new { error = ex.Message }, ctx.RequestAborted);
        }
        catch (LocalizedApiException ex)
        {
            ctx.Response.StatusCode = StatusCodes.Status409Conflict;
            await ctx.Response.WriteAsJsonAsync(new { error = ex.Message, errorCode = ex.ErrorCode, errorArgs = ex.Args }, ctx.RequestAborted);
        }
        catch (InvalidOperationException ex)
        {
            ctx.Response.StatusCode = StatusCodes.Status409Conflict;
            await ctx.Response.WriteAsJsonAsync(new { error = ex.Message }, ctx.RequestAborted);
        }
    }

    private static async Task<T> ReadJsonAsync<T>(HttpContext ctx) where T : class =>
        await ctx.Request.ReadFromJsonAsync<T>(ctx.RequestAborted)
        ?? throw new InvalidOperationException("请求体不能为空。");

    private static Guid GetId(HttpContext ctx) =>
        Guid.TryParse(ctx.Request.RouteValues["id"]?.ToString(), out var id)
            ? id
            : throw new InvalidOperationException("无效的 Id。");
}

