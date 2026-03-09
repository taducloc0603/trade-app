using Microsoft.Extensions.DependencyInjection;
using TradeDesktop.Application.Abstractions;
using TradeDesktop.Infrastructure.MarketData;
using TradeDesktop.Infrastructure.Signals;

namespace TradeDesktop.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<ISharedMemoryReader, MockSharedMemoryMarketDataReader>();
        services.AddSingleton<IMarketDataReader>(sp => sp.GetRequiredService<ISharedMemoryReader>());
        services.AddSingleton<ISignalEngine, SimpleSignalEngine>();
        return services;
    }
}