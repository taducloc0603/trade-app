using TradeDesktop.Application.Models;
using TradeDesktop.Application.Services;

namespace TradeDesktop.Tests;

public sealed class TradingFlowEngineTests
{
    private static readonly GapSignalConfirmationConfig Config = new(
        ConfirmGapPts: 5,
        OpenPts: 8,
        HoldConfirmMs: 500,
        CloseConfirmGapPts: 5,
        ClosePts: 8,
        CloseHoldConfirmMs: 400);

    [Fact]
    public void ProcessSnapshot_RunsSequentialFlow_OpenBuyThenCloseBuy()
    {
        var sut = new TradingFlowEngine(new GapSignalConfirmationEngine(), new CloseSignalEngine());
        var start = new DateTime(2026, 3, 18, 15, 20, 0, DateTimeKind.Utc);

        Assert.Equal(TradingFlowPhase.WaitingOpen, sut.CurrentPhase);
        Assert.Equal(TradingPositionSide.None, sut.CurrentPositionSide);

        Assert.Null(Process(sut, start.AddMilliseconds(0), gapBuy: 5, gapSell: null));
        Assert.Null(Process(sut, start.AddMilliseconds(100), gapBuy: 6, gapSell: null));
        Assert.Null(Process(sut, start.AddMilliseconds(300), gapBuy: 7, gapSell: null));

        var open = Process(sut, start.AddMilliseconds(520), gapBuy: 8, gapSell: null);
        Assert.NotNull(open);
        Assert.Equal(GapSignalAction.Open, open!.Action);
        Assert.Equal(GapSignalSide.Buy, open.Side);
        Assert.Equal(TradingFlowPhase.WaitingClose, sut.CurrentPhase);
        Assert.Equal(TradingPositionSide.Buy, sut.CurrentPositionSide);

        // WaitingClose: should ignore open checks even when GAP_BUY is strong.
        Assert.Null(Process(sut, start.AddMilliseconds(620), gapBuy: 20, gapSell: null));

        Assert.Null(Process(sut, start.AddMilliseconds(700), gapBuy: 20, gapSell: -5));
        Assert.Null(Process(sut, start.AddMilliseconds(860), gapBuy: 20, gapSell: -6));

        var close = Process(sut, start.AddMilliseconds(1110), gapBuy: 20, gapSell: -8);
        Assert.NotNull(close);
        Assert.Equal(GapSignalAction.Close, close!.Action);
        Assert.Equal(GapSignalSide.Buy, close.Side);
        Assert.Equal(TradingFlowPhase.WaitingOpen, sut.CurrentPhase);
        Assert.Equal(TradingPositionSide.None, sut.CurrentPositionSide);
    }

    [Fact]
    public void ProcessSnapshot_RunsSequentialFlow_OpenSellThenCloseSell()
    {
        var sut = new TradingFlowEngine(new GapSignalConfirmationEngine(), new CloseSignalEngine());
        var start = new DateTime(2026, 3, 18, 15, 25, 0, DateTimeKind.Utc);

        Assert.Null(Process(sut, start.AddMilliseconds(0), gapBuy: null, gapSell: -5));
        Assert.Null(Process(sut, start.AddMilliseconds(200), gapBuy: null, gapSell: -6));
        var open = Process(sut, start.AddMilliseconds(540), gapBuy: null, gapSell: -8);

        Assert.NotNull(open);
        Assert.Equal(GapSignalAction.Open, open!.Action);
        Assert.Equal(GapSignalSide.Sell, open.Side);
        Assert.Equal(TradingFlowPhase.WaitingClose, sut.CurrentPhase);
        Assert.Equal(TradingPositionSide.Sell, sut.CurrentPositionSide);

        Assert.Null(Process(sut, start.AddMilliseconds(640), gapBuy: 5, gapSell: -20));
        Assert.Null(Process(sut, start.AddMilliseconds(820), gapBuy: 6, gapSell: -20));
        var close = Process(sut, start.AddMilliseconds(1060), gapBuy: 8, gapSell: -20);

        Assert.NotNull(close);
        Assert.Equal(GapSignalAction.Close, close!.Action);
        Assert.Equal(GapSignalSide.Sell, close.Side);
        Assert.Equal(TradingFlowPhase.WaitingOpen, sut.CurrentPhase);
        Assert.Equal(TradingPositionSide.None, sut.CurrentPositionSide);
    }

    [Fact]
    public void Reset_ClearsPhaseAndPosition()
    {
        var sut = new TradingFlowEngine(new GapSignalConfirmationEngine(), new CloseSignalEngine());
        var start = new DateTime(2026, 3, 18, 15, 30, 0, DateTimeKind.Utc);

        _ = Process(sut, start.AddMilliseconds(0), gapBuy: 5, gapSell: null);
        _ = Process(sut, start.AddMilliseconds(200), gapBuy: 6, gapSell: null);
        _ = Process(sut, start.AddMilliseconds(550), gapBuy: 8, gapSell: null);

        Assert.Equal(TradingFlowPhase.WaitingClose, sut.CurrentPhase);
        Assert.Equal(TradingPositionSide.Buy, sut.CurrentPositionSide);

        sut.Reset();

        Assert.Equal(TradingFlowPhase.WaitingOpen, sut.CurrentPhase);
        Assert.Equal(TradingPositionSide.None, sut.CurrentPositionSide);
    }

    private static GapSignalTriggerResult? Process(
        TradingFlowEngine sut,
        DateTime timestampUtc,
        int? gapBuy,
        int? gapSell)
        => sut.ProcessSnapshot(
            new GapSignalSnapshot(timestampUtc, gapBuy, gapSell),
            Config);
}
