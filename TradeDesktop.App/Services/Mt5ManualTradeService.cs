using System.Globalization;
using TradeDesktop.App.Native;

namespace TradeDesktop.App.Services;

public sealed class Mt5ManualTradeService : IMt5ManualTradeService
{
    private const int CloseMaxAttempts = 25;
    private static readonly TimeSpan CloseRetryDelay = TimeSpan.FromMilliseconds(140);

    private readonly SemaphoreSlim _actionGate = new(1, 1);

    public Task<ManualTradeResult> ExecuteBuyAsync(string chartHwndA, string chartHwndB, CancellationToken cancellationToken = default)
        => ExecuteOpenAsync(
            label: "OPEN_MANUAL",
            chartHwndA,
            chartHwndB,
            actionA: "BUY",
            actionB: "SELL",
            clickA: NativeMethods.ClickBuy,
            clickB: NativeMethods.ClickSell,
            cancellationToken);

    public Task<ManualTradeResult> ExecuteSellAsync(string chartHwndA, string chartHwndB, CancellationToken cancellationToken = default)
        => ExecuteOpenAsync(
            label: "OPEN_MANUAL",
            chartHwndA,
            chartHwndB,
            actionA: "SELL",
            actionB: "BUY",
            clickA: NativeMethods.ClickSell,
            clickB: NativeMethods.ClickBuy,
            cancellationToken);

    public async Task<ManualTradeResult> ExecuteCloseAsync(string tradeHwndA, string tradeHwndB, CancellationToken cancellationToken = default)
    {
        await _actionGate.WaitAsync(cancellationToken);
        try
        {
            if (!TryParseHwnd(tradeHwndA, out var hwndA) || !TryParseHwnd(tradeHwndB, out var hwndB))
            {
                return new ManualTradeResult(
                    Label: "CLOSE_MANUAL",
                    Success: false,
                    Legs: [],
                    ErrorMessage: "HWND TRADE không hợp lệ. Vui lòng kiểm tra lại Config (định dạng 0x... hoặc số thập phân).");
            }

            if (!IsValidWindow(hwndA) || !IsValidWindow(hwndB))
            {
                return new ManualTradeResult(
                    Label: "CLOSE_MANUAL",
                    Success: false,
                    Legs: [],
                    ErrorMessage: "Một trong các TRADE HWND không còn hợp lệ.");
            }

            var legATask = CloseAllRowsBestEffortAsync("A", hwndA, cancellationToken);
            var legBTask = CloseAllRowsBestEffortAsync("B", hwndB, cancellationToken);
            var legs = await Task.WhenAll(legATask, legBTask);

            return new ManualTradeResult(
                Label: "CLOSE_MANUAL",
                Success: legs.All(l => l.Success),
                Legs: legs);
        }
        catch (Exception ex)
        {
            return new ManualTradeResult(
                Label: "CLOSE_MANUAL",
                Success: false,
                Legs: [],
                ErrorMessage: $"Lỗi close manual: {ex.Message}");
        }
        finally
        {
            _actionGate.Release();
        }
    }

    private async Task<ManualTradeResult> ExecuteOpenAsync(
        string label,
        string chartHwndA,
        string chartHwndB,
        string actionA,
        string actionB,
        Func<ulong, int> clickA,
        Func<ulong, int> clickB,
        CancellationToken cancellationToken)
    {
        await _actionGate.WaitAsync(cancellationToken);
        try
        {
            if (!TryParseHwnd(chartHwndA, out var hwndA) || !TryParseHwnd(chartHwndB, out var hwndB))
            {
                return new ManualTradeResult(
                    Label: label,
                    Success: false,
                    Legs: [],
                    ErrorMessage: "HWND CHART không hợp lệ. Vui lòng kiểm tra lại Config (định dạng 0x... hoặc số thập phân).");
            }

            if (!IsValidWindow(hwndA) || !IsValidWindow(hwndB))
            {
                return new ManualTradeResult(
                    Label: label,
                    Success: false,
                    Legs: [],
                    ErrorMessage: "Một trong các CHART HWND không còn hợp lệ.");
            }

            var legATask = Task.Run(() => ExecuteOpenLeg("A", actionA, hwndA, clickA), cancellationToken);
            var legBTask = Task.Run(() => ExecuteOpenLeg("B", actionB, hwndB, clickB), cancellationToken);
            var legs = await Task.WhenAll(legATask, legBTask);

            return new ManualTradeResult(
                Label: label,
                Success: legs.All(l => l.Success),
                Legs: legs);
        }
        catch (Exception ex)
        {
            return new ManualTradeResult(
                Label: label,
                Success: false,
                Legs: [],
                ErrorMessage: $"Lỗi open manual: {ex.Message}");
        }
        finally
        {
            _actionGate.Release();
        }
    }

    private static ManualTradeLegResult ExecuteOpenLeg(string exchange, string action, ulong chartHwnd, Func<ulong, int> click)
    {
        try
        {
            var ok = click(chartHwnd) == 1;
            return new ManualTradeLegResult(
                Exchange: exchange,
                Action: action,
                Success: ok,
                Detail: ok ? "clicked" : "click failed");
        }
        catch (Exception ex)
        {
            return new ManualTradeLegResult(exchange, action, false, ex.Message);
        }
    }

    private static async Task<ManualTradeLegResult> CloseAllRowsBestEffortAsync(string exchange, ulong tradeParentHwnd, CancellationToken cancellationToken)
    {
        IntPtr ctx = IntPtr.Zero;
        try
        {
            ctx = NativeMethods.CreateContextFromParent(tradeParentHwnd);
            if (ctx == IntPtr.Zero)
            {
                return new ManualTradeLegResult(exchange, "CLOSE", false, "create_context_from_parent failed");
            }

            for (var attempt = 0; attempt < CloseMaxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var rowCount = NativeMethods.UpdateRowCount(ctx);
                if (rowCount <= 0)
                {
                    return new ManualTradeLegResult(exchange, "CLOSE", true, $"closed all rows in {attempt} attempt(s)");
                }

                _ = NativeMethods.ClosePositionMt5(ctx, 0);
                await Task.Delay(CloseRetryDelay, cancellationToken);
            }

            var remainingRows = NativeMethods.UpdateRowCount(ctx);
            var success = remainingRows <= 0;
            return new ManualTradeLegResult(
                exchange,
                "CLOSE",
                success,
                success ? "closed all rows" : $"remaining rows: {remainingRows}");
        }
        catch (Exception ex)
        {
            return new ManualTradeLegResult(exchange, "CLOSE", false, ex.Message);
        }
        finally
        {
            if (ctx != IntPtr.Zero)
            {
                NativeMethods.DestroyContext(ctx);
            }
        }
    }

    private static bool TryParseHwnd(string raw, out ulong hwnd)
    {
        hwnd = 0;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var text = raw.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return ulong.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out hwnd);
        }

        return ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out hwnd);
    }

    private static bool IsValidWindow(ulong hwnd)
    {
        try
        {
            return NativeMethods.IsValidWindow(hwnd) == 1;
        }
        catch
        {
            return false;
        }
    }
}
