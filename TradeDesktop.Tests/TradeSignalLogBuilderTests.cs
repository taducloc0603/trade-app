using TradeDesktop.Application.Models;
using TradeDesktop.Application.Services;

namespace TradeDesktop.Tests;

public sealed class TradeSignalLogBuilderTests
{
    private readonly TradeInstructionFactory _instructionFactory = new();
    private readonly TradeSignalLogBuilder _logBuilder = new();

    [Fact]
    public void BuildLogLines_OpenBuy_PrintsAThenBWithCorrectSidesAndPrices()
    {
        var trigger = BuildTrigger(GapSignalAction.Open, GapSignalSide.Buy);

        var lines = _logBuilder.BuildLogLines(_instructionFactory.Create(trigger));

        Assert.Equal(2, lines.Count);
        Assert.Equal("[13:00:43.573] OPEN BUY A by GAP: 11 at Price: 4555.42 (29|2|2|20|29|11)", lines[0]);
        Assert.Equal("[13:00:43.573] OPEN SELL B by GAP: -22 at Price: 4555.67 (-26|-15|-19|-22|-22|-22)", lines[1]);
    }

    [Fact]
    public void BuildLogLines_OpenSell_PrintsAThenBWithCorrectSidesAndPrices()
    {
        var trigger = BuildTrigger(GapSignalAction.Open, GapSignalSide.Sell);

        var lines = _logBuilder.BuildLogLines(_instructionFactory.Create(trigger));

        Assert.Equal(2, lines.Count);
        Assert.Equal("[12:55:16.097] OPEN SELL A by GAP: -22 at Price: 4555.67 (-26|-15|-19|-22|-22|-22)", lines[0]);
        Assert.Equal("[12:55:16.097] OPEN BUY B by GAP: 11 at Price: 4555.42 (29|2|2|20|29|11)", lines[1]);
    }

    [Fact]
    public void BuildLogLines_CloseBuy_PrintsAThenBWithCorrectSidesAndPrices()
    {
        var trigger = BuildTrigger(GapSignalAction.Close, GapSignalSide.Buy);

        var lines = _logBuilder.BuildLogLines(_instructionFactory.Create(trigger));

        Assert.Equal(2, lines.Count);
        Assert.Equal("[12:57:43.347] CLOSE BUY A by GAP: 11 at Price: 4555.42 (29|2|2|20|29|11)", lines[0]);
        Assert.Equal("[12:57:43.347] CLOSE SELL B by GAP: -22 at Price: 4555.67 (-26|-15|-19|-22|-22|-22)", lines[1]);
    }

    [Fact]
    public void BuildLogLines_CloseSell_PrintsAThenBWithCorrectSidesAndPrices()
    {
        var trigger = BuildTrigger(GapSignalAction.Close, GapSignalSide.Sell);

        var lines = _logBuilder.BuildLogLines(_instructionFactory.Create(trigger));

        Assert.Equal(2, lines.Count);
        Assert.Equal("[12:53:15.737] CLOSE SELL A by GAP: -22 at Price: 4555.67 (-26|-15|-19|-22|-22|-22)", lines[0]);
        Assert.Equal("[12:53:15.737] CLOSE BUY B by GAP: 11 at Price: 4555.42 (29|2|2|20|29|11)", lines[1]);
    }

    private static GapSignalTriggerResult BuildTrigger(GapSignalAction action, GapSignalSide primarySide)
    {
        var triggeredAtUtc = new DateTime(2026, 3, 20, 6, 0, 43, 573, DateTimeKind.Utc);
        if (action == GapSignalAction.Open && primarySide == GapSignalSide.Sell)
        {
            triggeredAtUtc = new DateTime(2026, 3, 20, 5, 55, 16, 97, DateTimeKind.Utc);
        }
        else if (action == GapSignalAction.Close && primarySide == GapSignalSide.Buy)
        {
            triggeredAtUtc = new DateTime(2026, 3, 20, 5, 57, 43, 347, DateTimeKind.Utc);
        }
        else if (action == GapSignalAction.Close && primarySide == GapSignalSide.Sell)
        {
            triggeredAtUtc = new DateTime(2026, 3, 20, 5, 53, 15, 737, DateTimeKind.Utc);
        }

        return new GapSignalTriggerResult(
            Triggered: true,
            Action: action,
            PrimarySide: primarySide,
            BuyGaps: new[] { 29, 2, 2, 20, 29, 11 },
            SellGaps: new[] { -26, -15, -19, -22, -22, -22 },
            LastBuyGap: 11,
            LastSellGap: -22,
            TriggeredAtUtc: triggeredAtUtc,
            LastBid: 4555.42m,
            LastAsk: 4555.67m);
    }
}
