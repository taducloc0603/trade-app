using TradeDesktop.Application.Models;
using TradeDesktop.Application.Services;

namespace TradeDesktop.Tests;

public sealed class GapSignalConfirmationEngineTests
{
    private static readonly GapSignalConfirmationConfig DefaultConfig = new(
        ConfirmGapPts: 5,
        OpenPts: 8,
        HoldConfirmMs: 500);

    [Fact]
    public void ProcessSnapshot_TriggersBuyOpen_WhenHoldAndOpenConditionsSatisfied()
    {
        var sut = new GapSignalConfirmationEngine();
        var start = new DateTime(2026, 3, 17, 4, 35, 19, DateTimeKind.Utc);

        Assert.Empty(Process(sut, start.AddMilliseconds(0), gapBuy: 5, gapSell: null));
        Assert.Empty(Process(sut, start.AddMilliseconds(100), gapBuy: 6, gapSell: null));
        Assert.Empty(Process(sut, start.AddMilliseconds(200), gapBuy: 5, gapSell: null));
        Assert.Empty(Process(sut, start.AddMilliseconds(350), gapBuy: 7, gapSell: null));

        var results = Process(sut, start.AddMilliseconds(500), gapBuy: 8, gapSell: null);

        var trigger = Assert.Single(results);
        Assert.Equal(GapSignalAction.Open, trigger.Action);
        Assert.Equal(GapSignalSide.Buy, trigger.Side);
        Assert.Equal(new[] { 5, 6, 5, 7, 8 }, trigger.Gaps);
        Assert.Equal(2945.12m, trigger.TriggerPrice);
    }

    [Fact]
    public void ProcessSnapshot_TriggersSellOpen_WhenHoldAndOpenConditionsSatisfied()
    {
        var sut = new GapSignalConfirmationEngine();
        var start = new DateTime(2026, 3, 17, 4, 35, 24, DateTimeKind.Utc);

        Assert.Empty(Process(sut, start.AddMilliseconds(0), gapBuy: null, gapSell: -5));
        Assert.Empty(Process(sut, start.AddMilliseconds(100), gapBuy: null, gapSell: -6));
        Assert.Empty(Process(sut, start.AddMilliseconds(220), gapBuy: null, gapSell: -7));

        var results = Process(sut, start.AddMilliseconds(540), gapBuy: null, gapSell: -8);

        var trigger = Assert.Single(results);
        Assert.Equal(GapSignalAction.Open, trigger.Action);
        Assert.Equal(GapSignalSide.Sell, trigger.Side);
        Assert.Equal(new[] { -5, -6, -7, -8 }, trigger.Gaps);
        Assert.Equal(2945.34m, trigger.TriggerPrice);
    }

    [Fact]
    public void ProcessSnapshot_DoesNotTrigger_WhenOneTickFailsConfirmCondition()
    {
        var sut = new GapSignalConfirmationEngine();
        var start = new DateTime(2026, 3, 17, 4, 35, 19, DateTimeKind.Utc);

        Assert.Empty(Process(sut, start.AddMilliseconds(0), gapBuy: 5, gapSell: null));
        Assert.Empty(Process(sut, start.AddMilliseconds(200), gapBuy: 6, gapSell: null));

        // Fail confirm -> must reset window.
        Assert.Empty(Process(sut, start.AddMilliseconds(300), gapBuy: 4, gapSell: null));

        Assert.Empty(Process(sut, start.AddMilliseconds(400), gapBuy: 7, gapSell: null));
        var results = Process(sut, start.AddMilliseconds(800), gapBuy: 8, gapSell: null);

        Assert.Empty(results);
    }

    [Fact]
    public void ProcessSnapshot_DoesNotTrigger_WhenLastGapDoesNotMeetOpenCondition()
    {
        var sut = new GapSignalConfirmationEngine();
        var start = new DateTime(2026, 3, 17, 4, 35, 19, DateTimeKind.Utc);

        Assert.Empty(Process(sut, start.AddMilliseconds(0), gapBuy: 5, gapSell: null));
        Assert.Empty(Process(sut, start.AddMilliseconds(200), gapBuy: 6, gapSell: null));
        Assert.Empty(Process(sut, start.AddMilliseconds(400), gapBuy: 7, gapSell: null));

        // Hold reached but open not reached (7 < 8) -> reset.
        var results = Process(sut, start.AddMilliseconds(600), gapBuy: 7, gapSell: null);

        Assert.Empty(results);
    }

    [Fact]
    public void ProcessSnapshot_ResetsStateAfterTrigger_ToAvoidDuplicateReuseOfOldWindow()
    {
        var sut = new GapSignalConfirmationEngine();
        var start = new DateTime(2026, 3, 17, 4, 35, 19, DateTimeKind.Utc);

        _ = Process(sut, start.AddMilliseconds(0), gapBuy: 5, gapSell: null);
        _ = Process(sut, start.AddMilliseconds(100), gapBuy: 6, gapSell: null);
        _ = Process(sut, start.AddMilliseconds(200), gapBuy: 5, gapSell: null);
        _ = Process(sut, start.AddMilliseconds(350), gapBuy: 7, gapSell: null);

        var firstTrigger = Process(sut, start.AddMilliseconds(500), gapBuy: 8, gapSell: null);
        Assert.Single(firstTrigger);

        // New cycle starts fresh after trigger.
        Assert.Empty(Process(sut, start.AddMilliseconds(600), gapBuy: 9, gapSell: null));

        var secondTrigger = Process(sut, start.AddMilliseconds(1200), gapBuy: 9, gapSell: null);
        var trigger = Assert.Single(secondTrigger);
        Assert.Equal(new[] { 9, 9 }, trigger.Gaps);
    }

    private static IReadOnlyList<GapSignalTriggerResult> Process(
        GapSignalConfirmationEngine sut,
        DateTime timestampUtc,
        int? gapBuy,
        int? gapSell)
        => sut.ProcessSnapshot(
            new GapSignalSnapshot(timestampUtc, 2945.12m, 2945.34m, gapBuy, gapSell),
            DefaultConfig);
}
