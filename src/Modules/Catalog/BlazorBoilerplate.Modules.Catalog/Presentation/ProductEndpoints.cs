using System.Globalization;
using BlazorBoilerplate.Application.OData;
using BlazorBoilerplate.Modules.Catalog.Application;
using BlazorBoilerplate.ModuleAbstractions;
using Microsoft.AspNetCore.Http;

namespace BlazorBoilerplate.Modules.Catalog.Presentation;

/// <summary>模块端点。RequestDelegate 被运行时包装以支持 Drain 计数。</summary>
public sealed class ProductEndpoints(ProductService service)
{
    public void Map(IModuleEndpointBuilder builder)
    {
        builder.Map("/api/catalog/products", "GET", "catalog.product.read", ListAsync);
        builder.Map("/api/catalog/products/{id}", "GET", "catalog.product.read", GetByIdAsync);
        builder.Map("/api/catalog/stats", "GET", "catalog.product.read", GetStatsAsync);
    }

    private async Task GetStatsAsync(HttpContext context)
    {
        var stats = await service.GetStatsAsync(context.RequestAborted);
        await context.Response.WriteAsJsonAsync(stats, context.RequestAborted);
    }

    private async Task ListAsync(HttpContext context)
    {
        var query = ParseQuery(context);
        var page = await service.QueryAsync(query, context.RequestAborted);
        await context.Response.WriteAsJsonAsync(page, context.RequestAborted);
    }

    private async Task GetByIdAsync(HttpContext context)
    {
        if (!Guid.TryParse(context.Request.RouteValues["id"]?.ToString(), out var id))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid product id." }, context.RequestAborted);
            return;
        }

        var filter = new ODataQueryBuilder(new ODataFieldMap(["id"]))
            .Equal("id", id.ToString()).Top(1).Build();
        var all = await service.QueryAsync(filter, context.RequestAborted);
        if (all.Value.Count == 0)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        await context.Response.WriteAsJsonAsync(all.Value[0], context.RequestAborted);
    }

    private static ODataQuery ParseQuery(HttpContext context)
    {
        var query = context.Request.Query;
        int? ParseInt(string key) =>
            int.TryParse(query[key].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;

        var filter = query["$filter"].ToString();
        if (filter.Length > ODataLimits.MaxFilterLength)
            throw new InvalidOperationException($"$filter exceeds {ODataLimits.MaxFilterLength} characters.");

        return new ODataQuery
        {
            Filter = string.IsNullOrWhiteSpace(filter) ? null : filter,
            OrderBy = query["$orderby"].ToString() is { Length: > 0 } orderBy ? orderBy : null,
            Top = ParseInt("$top"),
            Skip = ParseInt("$skip"),
            Count = query["$count"].ToString() == "true"
        };
    }
}
