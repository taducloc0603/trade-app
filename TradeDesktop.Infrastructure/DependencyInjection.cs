using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradeDesktop.Application.Abstractions;
using TradeDesktop.Infrastructure.MarketData;
using TradeDesktop.Infrastructure.Signals;
using TradeDesktop.Infrastructure.Supabase;

namespace TradeDesktop.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ISharedMemoryReader, MockSharedMemoryMarketDataReader>();
        services.AddSingleton<IMarketDataReader>(sp => sp.GetRequiredService<ISharedMemoryReader>());
        services.AddSingleton<ISignalEngine, SimpleSignalEngine>();
        services.AddHttpClient();
        services.AddSingleton<IConfigRepository>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient();

            var url =
                configuration["SUPABASE_URL"] ??
                configuration["NEXT_PUBLIC_SUPABASE_URL"] ??
                configuration["Supabase:Url"] ??
                configuration["Supabase__Url"];

            var key =
                configuration["SUPABASE_KEY"] ??
                configuration["SUPABASE_ANON_KEY"] ??
                configuration["NEXT_PUBLIC_SUPABASE_ANON_KEY"] ??
                configuration["SUPABASE_SERVICE_ROLE_KEY"] ??
                configuration["Supabase:Key"] ??
                configuration["Supabase__Key"];

            return new SupabaseConfigRepository(httpClient, url, key);
        });

        return services;
    }
}