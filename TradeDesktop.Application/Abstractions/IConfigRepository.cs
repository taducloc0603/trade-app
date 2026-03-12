using TradeDesktop.Application.Models;

namespace TradeDesktop.Application.Abstractions;

public interface IConfigRepository
{
    Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<ConfigRecord?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<bool> UpdateSansAndIpByCodeAsync(string code, string sansJson, string ip, CancellationToken cancellationToken = default);
}
