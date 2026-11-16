using BlazorBoilerplate.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlazorBoilerplate.Modules.Identity.Persistence;

public static class IdentityDbContextOptions
{
    public static DbContextOptionsBuilder UseIdentityProvider(
        this DbContextOptionsBuilder builder,
        IServiceProvider? services = null,
        string? provider = null,
        string? connectionString = null,
        bool enableRetryOnFailure = false)
        => builder.UsePlatformDbProvider(services, provider, connectionString, enableRetryOnFailure);
}
