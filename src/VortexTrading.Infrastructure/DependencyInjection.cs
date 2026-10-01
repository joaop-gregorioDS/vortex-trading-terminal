using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace VortexTrading.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Adapters will be registered in their dedicated implementation tasks.
        return services;
    }
}
