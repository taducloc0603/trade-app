namespace TradeDesktop.Application.Abstractions;

public interface IConfigRepository
{
    Task<bool> ExistsByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> UpdateSansAsync(string id, string mapName1, string mapName2, CancellationToken cancellationToken = default);
}
