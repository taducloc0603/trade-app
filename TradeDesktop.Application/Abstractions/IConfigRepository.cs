using TradeDesktop.Application.Models;

namespace TradeDesktop.Application.Abstractions;

public interface IConfigRepository
{
    Task<ConfigRecord?> GetByHostNameAsync(string hostName, CancellationToken cancellationToken = default);
    Task<bool> UpdateSansAndHostNameByHostNameAsync(string hostName, string sansJson, CancellationToken cancellationToken = default);
}
