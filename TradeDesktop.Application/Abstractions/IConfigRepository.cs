using TradeDesktop.Application.Models;

namespace TradeDesktop.Application.Abstractions;

public interface IConfigRepository
{
    Task<ConfigRecord?> GetByIpAsync(string ip, CancellationToken cancellationToken = default);
    Task<bool> UpdateSansAndIpByIpAsync(string ip, string sansJson, CancellationToken cancellationToken = default);
}
