using TradeDesktop.Application.Models;
using TradeDesktop.Application.Abstractions;

namespace TradeDesktop.Application.Services;

public interface IGapCalculator
{
    (int? GapBuy, int? GapSell) Calculate(ExchangeMetrics sanA, ExchangeMetrics sanB);
}

public sealed class GapCalculator(IRuntimeConfigProvider runtimeConfigProvider) : IGapCalculator
{
    public (int? GapBuy, int? GapSell) Calculate(ExchangeMetrics sanA, ExchangeMetrics sanB)
    {
        var pointMultiplier = runtimeConfigProvider.CurrentPoint > 0
            ? runtimeConfigProvider.CurrentPoint
            : 1;

        int? gapBuy = null;
        int? gapSell = null;

        if (sanB.Bid.HasValue && sanA.Ask.HasValue)
        {
            var value = (sanB.Bid.Value - sanA.Ask.Value) * pointMultiplier;
            gapBuy = decimal.ToInt32(decimal.Round(value, 0, MidpointRounding.AwayFromZero));
        }

        if (sanA.Bid.HasValue && sanB.Ask.HasValue)
        {
            var value = (sanA.Bid.Value - sanB.Ask.Value) * pointMultiplier;
            gapSell = decimal.ToInt32(decimal.Round(value, 0, MidpointRounding.AwayFromZero));
        }

        return (gapBuy, gapSell);
    }
}
