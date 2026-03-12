using TradeDesktop.Application.Models;

namespace TradeDesktop.Application.Services;

public interface IGapCalculator
{
    (decimal? GapBuy, decimal? GapSell) Calculate(ExchangeMetrics sanA, ExchangeMetrics sanB);
}

public sealed class GapCalculator : IGapCalculator
{
    public (decimal? GapBuy, decimal? GapSell) Calculate(ExchangeMetrics sanA, ExchangeMetrics sanB)
    {
        decimal? gapBuy = null;
        decimal? gapSell = null;

        if (sanB.Bid.HasValue && sanA.Ask.HasValue)
        {
            gapBuy = sanB.Bid.Value - sanA.Ask.Value;
        }

        if (sanA.Bid.HasValue && sanB.Ask.HasValue)
        {
            gapSell = sanA.Bid.Value - sanB.Ask.Value;
        }

        return (gapBuy, gapSell);
    }
}
