using Microsoft.Extensions.DependencyInjection;
using TradeDesktop.Application.Abstractions;
using TradeDesktop.Application.Services;

namespace TradeDesktop.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IDashboardService, DashboardService>();
        services.AddSingleton<IConfigService, ConfigService>();
        services.AddSingleton<IGapCalculator, GapCalculator>();
        services.AddSingleton<IDashboardMetricsMapper, DashboardMetricsMapper>();
        return services;
    }
}