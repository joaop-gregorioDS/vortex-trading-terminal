using Microsoft.Extensions.DependencyInjection;
using VortexTrading.Application.Charts;

namespace VortexTrading.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<HistoricalChartQuery>();
        return services;
    }
}
