using TradeDesktop.Application.Models;
using TradeDesktop.Application.Abstractions;

namespace TradeDesktop.Application.Services;

public interface IGapCalculator
{
    (decimal? GapBuy, decimal? GapSell) Calculate(ExchangeMetrics sanA, ExchangeMetrics sanB);
}

public sealed class GapCalculator(IRuntimeConfigProvider runtimeConfigProvider) : IGapCalculator
{
    public (decimal? GapBuy, decimal? GapSell) Calculate(ExchangeMetrics sanA, ExchangeMetrics sanB)
    {
        var pointMultiplier = runtimeConfigProvider.CurrentPoint > 0
            ? runtimeConfigProvider.CurrentPoint
            : 1;

        decimal? gapBuy = null;
        decimal? gapSell = null;

        if (sanB.Bid.HasValue && sanA.Ask.HasValue)
        {
            gapBuy = (sanB.Bid.Value - sanA.Ask.Value) * pointMultiplier;
        }

        if (sanA.Bid.HasValue && sanB.Ask.HasValue)
        {
            gapSell = (sanA.Bid.Value - sanB.Ask.Value) * pointMultiplier;
        }

        return (gapBuy, gapSell);
    }
}
