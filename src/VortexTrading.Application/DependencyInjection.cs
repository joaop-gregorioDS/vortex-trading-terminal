using Microsoft.Extensions.DependencyInjection;

namespace VortexTrading.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Application services will be registered when use cases are introduced.
        return services;
    }
}
