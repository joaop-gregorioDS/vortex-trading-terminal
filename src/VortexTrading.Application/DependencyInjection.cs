using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VortexTrading.Application.Charts;
using VortexTrading.Application.Risk;

namespace VortexTrading.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<HistoricalChartQuery>();
        services.AddSingleton<IRiskEvaluator>(provider =>
        {
            var options = provider.GetService<IOptions<RiskOptions>>()?.Value ?? new RiskOptions();
            return RiskEvaluator.FromOptions(options);
        });

        return services;
    }

    public static IServiceCollection AddRiskManagement(
        this IServiceCollection services,
        Action<RiskOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new RiskOptions();
        configure?.Invoke(options);

        services.AddSingleton<IRiskEvaluator>(RiskEvaluator.FromOptions(options));
        return services;
    }
}

