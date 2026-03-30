using System.Globalization;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using TradeDesktop.App.Commands;
using TradeDesktop.App.Helpers;
using TradeDesktop.App.Services;
using TradeDesktop.App.State;
using TradeDesktop.Application.Abstractions;
using TradeDesktop.Application.Models;
using TradeDesktop.Application.Services;
using TradeDesktop.Domain.Models;

namespace TradeDesktop.App.ViewModels;

public sealed class DashboardViewModel : ObservableObject
{
    private readonly IServiceProvider _serviceProvider;
    private readonly RuntimeConfigState _runtimeConfigState;
    private readonly IConfigService _configService;
    private readonly IDashboardMetricsMapper _dashboardMetricsMapper;
    private readonly ITradingFlowEngine _tradingFlowEngine;
    private readonly ITradeInstructionFactory _tradeInstructionFactory;
    private readonly ITradeSignalLogBuilder _tradeSignalLogBuilder;
    private readonly IMachineIdentityService _machineIdentityService;
    private readonly ITradesSharedMemoryReader _tradesSharedMemoryReader;
    private readonly IHistorySharedMemoryReader _historySharedMemoryReader;
    private readonly IMt5ManualTradeService _mt5ManualTradeService;
    private readonly string _normalizedHostName;
    private readonly CancellationTokenSource _orderInfoPollingCts = new();
    private readonly Dictionary<string, ulong> _lastTradeTimestampByMap = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ulong> _lastHistoryTimestampByMap = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<ulong>> _knownTradeTicketsByMap = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<ulong>> _knownHistoryTicketsByMap = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<PendingOpenRequest>> _pendingOpenRequestsByMap = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<PendingCloseRequest>> _pendingCloseRequestsByMap = new(StringComparer.Ordinal);
    private readonly Dictionary<ulong, PendingOpenRequest> _openRequestByTicket = [];
    private readonly Dictionary<ulong, PendingCloseRequest> _closeRequestByTicket = [];
    private readonly Dictionary<ulong, double> _openSlippageByTicket = [];
    private readonly Dictionary<ulong, long> _openExecutionMsByTicket = [];
    private readonly Dictionary<ulong, long> _closeExecutionMsByTicket = [];
    private SharedMapReadResult<TradeSharedRecord>? _latestTradeLeftResult;
    private SharedMapReadResult<TradeSharedRecord>? _latestTradeRightResult;

    private static readonly TimeSpan OrderInfoPollInterval = TimeSpan.FromSeconds(1);

    private sealed record PendingOpenRequest(
        string TradeMapName,
        string? Symbol,
        int TradeType,
        double? Volume,
        double ExpectedPrice,
        DateTimeOffset AppOpenRequestTimeLocal,
        long AppOpenRequestUnixMs,
        long AppOpenRequestRawMs);

    private sealed record PendingCloseRequest(
        string TradeMapName,
        ulong? Ticket,
        string? Symbol,
        int TradeType,
        double? Volume,
        double ExpectedPrice,
        DateTimeOffset AppCloseRequestTimeLocal,
        long AppCloseRequestUnixMs,
        long AppCloseRequestRawMs);

    private string _runtimeSummary = string.Empty;
    private string _dbInlineData = string.Empty;
    private bool _isDbInlineDataVisible;
    private string _configErrorMessage = string.Empty;
    private bool _isConfigErrorVisible;
    private string _exchangeAHeader = "Sàn A";
    private string _exchangeBHeader = "Sàn B";
    private string _gapBuy = "-";
    private string _gapSell = "-";

    private string _exchangeASymbol = "-";
    private string _exchangeABid = "-";
    private string _exchangeAAsk = "-";
    private string _exchangeASpread = "-";
    private string _exchangeALatencyMs = "-";
    private string _exchangeATps = "-";
    private string _exchangeATime = "-";
    private string _exchangeAMaxLatMs = "-";
    private string _exchangeAAvgLatMs = "-";

    private string _exchangeBSymbol = "-";
    private string _exchangeBBid = "-";
    private string _exchangeBAsk = "-";
    private string _exchangeBSpread = "-";
    private string _exchangeBLatencyMs = "-";
    private string _exchangeBTps = "-";
    private string _exchangeBTime = "-";
    private string _exchangeBMaxLatMs = "-";
    private string _exchangeBAvgLatMs = "-";
    private bool _isTradingLogicEnabled;
    private string _lastSignalText = "-";
    private bool _isLoading = true;
    private string _loadingMessage = "Đang chờ dữ liệu shared memory...";
    private string _machineHostName = string.Empty;
    private bool _hasManualTradeHwndConfig;
    private OrderTabType _selectedOrderTab = OrderTabType.Trade;
    private int _selectedOrderTabIndex;

    public DashboardViewModel(
        IServiceProvider serviceProvider,
        RuntimeConfigState runtimeConfigState,
        IConfigService configService,
        IExchangePairReader exchangePairReader,
        IDashboardMetricsMapper dashboardMetricsMapper,
        ITradingFlowEngine tradingFlowEngine,
        ITradeInstructionFactory tradeInstructionFactory,
        ITradeSignalLogBuilder tradeSignalLogBuilder,
        IMachineIdentityService machineIdentityService,
        ITradesSharedMemoryReader tradesSharedMemoryReader,
        IHistorySharedMemoryReader historySharedMemoryReader,
        IMt5ManualTradeService mt5ManualTradeService)
    {
        _serviceProvider = serviceProvider;
        _runtimeConfigState = runtimeConfigState;
        _configService = configService;
        _dashboardMetricsMapper = dashboardMetricsMapper;
        _tradingFlowEngine = tradingFlowEngine;
        _tradeInstructionFactory = tradeInstructionFactory;
        _tradeSignalLogBuilder = tradeSignalLogBuilder;
        _machineIdentityService = machineIdentityService;
        _tradesSharedMemoryReader = tradesSharedMemoryReader;
        _historySharedMemoryReader = historySharedMemoryReader;
        _mt5ManualTradeService = mt5ManualTradeService;

        var normalizedHostName = _machineIdentityService.GetHostName();
        _normalizedHostName = normalizedHostName;
        MachineHostName = normalizedHostName;

        OpenConfigCommand = new AsyncRelayCommand(OpenConfigAsync);
        ReconnectConfigCommand = new AsyncRelayCommand(ReconnectConfigAsync);
        CopyHostNameCommand = new AsyncRelayCommand(CopyHostNameAsync);
        StartTradingLogicCommand = new AsyncRelayCommand(StartTradingLogicAsync, CanStartTradingLogic);
        StopTradingLogicCommand = new AsyncRelayCommand(StopTradingLogicAsync, CanStopTradingLogic);
        BuyCommand = new AsyncRelayCommand(BuyAsync);
        SellCommand = new AsyncRelayCommand(SellAsync);
        CloseOrderCommand = new AsyncRelayCommand(CloseOrderAsync);

        TradeTab = new OrderInfoTabViewModel(
            OrderTabType.Trade,
            "Trade",
            new OrderPanelStatusViewModel("Sàn A", OrderRecordLayoutMode.TradeTable),
            new OrderPanelStatusViewModel("Sàn B", OrderRecordLayoutMode.TradeTable));

        HistoryTab = new OrderInfoTabViewModel(
            OrderTabType.History,
            "History",
            new OrderPanelStatusViewModel("Sàn A", OrderRecordLayoutMode.HistoryTable),
            new OrderPanelStatusViewModel("Sàn B", OrderRecordLayoutMode.HistoryTable));

        OrderTabs = [TradeTab, HistoryTab];

        _runtimeConfigState.StateChanged += (_, _) => ApplyRuntimeConfig();
        ApplyRuntimeConfig();
        _ = InitializeRuntimeConfigAsync();

        exchangePairReader.SnapshotReceived += OnSnapshotReceived;
        _ = StartExchangeReaderSafeAsync(exchangePairReader);
        _ = RunOrderInfoPollingAsync(_orderInfoPollingCts.Token);
    }

    public string RuntimeSummary
    {
        get => _runtimeSummary;
        private set => SetProperty(ref _runtimeSummary, value);
    }

    public string DbInlineData
    {
        get => _dbInlineData;
        private set => SetProperty(ref _dbInlineData, value);
    }

    public bool IsDbInlineDataVisible
    {
        get => _isDbInlineDataVisible;
        private set => SetProperty(ref _isDbInlineDataVisible, value);
    }

    public string ConfigErrorMessage
    {
        get => _configErrorMessage;
        private set => SetProperty(ref _configErrorMessage, value);
    }

    public bool IsConfigErrorVisible
    {
        get => _isConfigErrorVisible;
        private set => SetProperty(ref _isConfigErrorVisible, value);
    }

    public string ExchangeAHeader
    {
        get => _exchangeAHeader;
        private set => SetProperty(ref _exchangeAHeader, value);
    }

    public string ExchangeBHeader
    {
        get => _exchangeBHeader;
        private set => SetProperty(ref _exchangeBHeader, value);
    }

    public string GapBuy
    {
        get => _gapBuy;
        private set => SetProperty(ref _gapBuy, value);
    }

    public string GapSell
    {
        get => _gapSell;
        private set => SetProperty(ref _gapSell, value);
    }

    public string ExchangeASymbol { get => _exchangeASymbol; private set => SetProperty(ref _exchangeASymbol, value); }
    public string ExchangeABid { get => _exchangeABid; private set => SetProperty(ref _exchangeABid, value); }
    public string ExchangeAAsk { get => _exchangeAAsk; private set => SetProperty(ref _exchangeAAsk, value); }
    public string ExchangeASpread { get => _exchangeASpread; private set => SetProperty(ref _exchangeASpread, value); }
    public string ExchangeALatencyMs { get => _exchangeALatencyMs; private set => SetProperty(ref _exchangeALatencyMs, value); }
    public string ExchangeATps { get => _exchangeATps; private set => SetProperty(ref _exchangeATps, value); }
    public string ExchangeATime { get => _exchangeATime; private set => SetProperty(ref _exchangeATime, value); }
    public string ExchangeAMaxLatMs { get => _exchangeAMaxLatMs; private set => SetProperty(ref _exchangeAMaxLatMs, value); }
    public string ExchangeAAvgLatMs { get => _exchangeAAvgLatMs; private set => SetProperty(ref _exchangeAAvgLatMs, value); }

    public string ExchangeBSymbol { get => _exchangeBSymbol; private set => SetProperty(ref _exchangeBSymbol, value); }
    public string ExchangeBBid { get => _exchangeBBid; private set => SetProperty(ref _exchangeBBid, value); }
    public string ExchangeBAsk { get => _exchangeBAsk; private set => SetProperty(ref _exchangeBAsk, value); }
    public string ExchangeBSpread { get => _exchangeBSpread; private set => SetProperty(ref _exchangeBSpread, value); }
    public string ExchangeBLatencyMs { get => _exchangeBLatencyMs; private set => SetProperty(ref _exchangeBLatencyMs, value); }
    public string ExchangeBTps { get => _exchangeBTps; private set => SetProperty(ref _exchangeBTps, value); }
    public string ExchangeBTime { get => _exchangeBTime; private set => SetProperty(ref _exchangeBTime, value); }
    public string ExchangeBMaxLatMs { get => _exchangeBMaxLatMs; private set => SetProperty(ref _exchangeBMaxLatMs, value); }
    public string ExchangeBAvgLatMs { get => _exchangeBAvgLatMs; private set => SetProperty(ref _exchangeBAvgLatMs, value); }
    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }
    public string LoadingMessage { get => _loadingMessage; private set => SetProperty(ref _loadingMessage, value); }
    public string MachineHostName { get => _machineHostName; private set => SetProperty(ref _machineHostName, value); }

    public bool HasManualTradeHwndConfig
    {
        get => _hasManualTradeHwndConfig;
        private set
        {
            if (!SetProperty(ref _hasManualTradeHwndConfig, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsManualTradeWarningVisible));
        }
    }

    public bool IsManualTradeWarningVisible => !HasManualTradeHwndConfig;

    public string ManualTradeWarningMessage =>
        "Vui lòng nhập đầy đủ CHART/TRADE HWND cho sàn A và sàn B trong Config";

    public string LastSignalText
    {
        get => _lastSignalText;
        private set => SetProperty(ref _lastSignalText, value);
    }

    public ObservableCollection<string> SignalLogItems { get; } = [];
    public IReadOnlyList<OrderInfoTabViewModel> OrderTabs { get; }
    public OrderInfoTabViewModel TradeTab { get; }
    public OrderInfoTabViewModel HistoryTab { get; }

    public OrderTabType SelectedOrderTab
    {
        get => _selectedOrderTab;
        set
        {
            if (!SetProperty(ref _selectedOrderTab, value))
            {
                return;
            }

            var tabIndex = value == OrderTabType.History ? 1 : 0;
            if (_selectedOrderTabIndex != tabIndex)
            {
                _selectedOrderTabIndex = tabIndex;
                OnPropertyChanged(nameof(SelectedOrderTabIndex));
            }
        }
    }

    public int SelectedOrderTabIndex
    {
        get => _selectedOrderTabIndex;
        set
        {
            if (!SetProperty(ref _selectedOrderTabIndex, value))
            {
                return;
            }

            SelectedOrderTab = value == 1 ? OrderTabType.History : OrderTabType.Trade;
        }
    }

    public bool IsTradingLogicEnabled
    {
        get => _isTradingLogicEnabled;
        private set
        {
            if (!SetProperty(ref _isTradingLogicEnabled, value))
            {
                return;
            }

            OnPropertyChanged(nameof(TradingLogicStatusText));
            OnPropertyChanged(nameof(TradingLogicStatusBrush));
            StartTradingLogicCommand.RaiseCanExecuteChanged();
            StopTradingLogicCommand.RaiseCanExecuteChanged();
        }
    }

    public string TradingLogicStatusText => IsTradingLogicEnabled ? "Running" : "Stopped";
    public Brush TradingLogicStatusBrush => IsTradingLogicEnabled ? Brushes.ForestGreen : Brushes.Gray;
    public string CurrentPositionText => ResolveCurrentPositionText();
    public string CurrentPhaseText => ResolveCurrentPhaseText();

    public AsyncRelayCommand OpenConfigCommand { get; }
    public AsyncRelayCommand ReconnectConfigCommand { get; }
    public AsyncRelayCommand CopyHostNameCommand { get; }
    public AsyncRelayCommand StartTradingLogicCommand { get; }
    public AsyncRelayCommand StopTradingLogicCommand { get; }
    public IAsyncRelayCommand BuyCommand { get; }
    public IAsyncRelayCommand SellCommand { get; }
    public IAsyncRelayCommand CloseOrderCommand { get; }

    private bool CanStartTradingLogic() => !IsTradingLogicEnabled;
    private bool CanStopTradingLogic() => IsTradingLogicEnabled;

    private Task StartTradingLogicAsync()
    {
        if (IsTradingLogicEnabled)
        {
            return Task.CompletedTask;
        }

        ResetTradingLogicState();
        IsTradingLogicEnabled = true;
        LastSignalText = "-";
        SignalLogItems.Clear();
        return Task.CompletedTask;
    }

    private Task StopTradingLogicAsync()
    {
        if (!IsTradingLogicEnabled)
        {
            return Task.CompletedTask;
        }

        IsTradingLogicEnabled = false;
        ResetTradingLogicState();
        LastSignalText = "-";
        return Task.CompletedTask;
    }

    private async Task BuyAsync()
    {
        var snapshot = _runtimeConfigState.CurrentDashboardMetrics;
        var appOpenRequestTimeLocal = DateTimeOffset.Now;
        var appOpenRequestRawMs = Environment.TickCount64;

        // Capture pending request BEFORE executing click to avoid race with shared-memory polling.
        CapturePendingOpenRequest(TradeTab.LeftPanel.TargetMapName, snapshot, isExchangeA: true, tradeType: 0, appOpenRequestTimeLocal, appOpenRequestRawMs);
        CapturePendingOpenRequest(TradeTab.RightPanel.TargetMapName, snapshot, isExchangeA: false, tradeType: 1, appOpenRequestTimeLocal, appOpenRequestRawMs);

        var result = await _mt5ManualTradeService.ExecuteBuyAsync(
            _runtimeConfigState.CurrentChartHwndA,
            _runtimeConfigState.CurrentChartHwndB);

        AppendManualTradeLogs(result, snapshot);
        ShowManualTradeFeedback("BUY", result);
    }

    private async Task SellAsync()
    {
        var snapshot = _runtimeConfigState.CurrentDashboardMetrics;
        var appOpenRequestTimeLocal = DateTimeOffset.Now;
        var appOpenRequestRawMs = Environment.TickCount64;

        // Capture pending request BEFORE executing click to avoid race with shared-memory polling.
        CapturePendingOpenRequest(TradeTab.LeftPanel.TargetMapName, snapshot, isExchangeA: true, tradeType: 1, appOpenRequestTimeLocal, appOpenRequestRawMs);
        CapturePendingOpenRequest(TradeTab.RightPanel.TargetMapName, snapshot, isExchangeA: false, tradeType: 0, appOpenRequestTimeLocal, appOpenRequestRawMs);

        var result = await _mt5ManualTradeService.ExecuteSellAsync(
            _runtimeConfigState.CurrentChartHwndA,
            _runtimeConfigState.CurrentChartHwndB);

        AppendManualTradeLogs(result, snapshot);
        ShowManualTradeFeedback("SELL", result);
    }

    private async Task CloseOrderAsync()
    {
        var snapshot = _runtimeConfigState.CurrentDashboardMetrics;
        var selectA = SelectCloseCandidateForExchange(
            exchangeLabel: "A",
            tradeMapName: TradeTab.LeftPanel.TargetMapName,
            tradeHwnd: _runtimeConfigState.CurrentTradeHwndA);

        var selectB = SelectCloseCandidateForExchange(
            exchangeLabel: "B",
            tradeMapName: TradeTab.RightPanel.TargetMapName,
            tradeHwnd: _runtimeConfigState.CurrentTradeHwndB);

        var appCloseRequestTimeLocal = DateTimeOffset.Now;
        var appCloseRequestRawMs = Environment.TickCount64;

        // Capture pending request BEFORE executing close to avoid race with shared-memory polling.
        CapturePendingCloseRequest(selectA, snapshot, isExchangeA: true, appCloseRequestTimeLocal, appCloseRequestRawMs);
        CapturePendingCloseRequest(selectB, snapshot, isExchangeA: false, appCloseRequestTimeLocal, appCloseRequestRawMs);

        var result = await _mt5ManualTradeService.ExecuteCloseAsync(
            selectA.Request,
            selectB.Request);

        AppendManualTradeLogs(result, snapshot);
        AppendCloseSelectionDiagnostics(selectA, selectB);
        ShowManualTradeFeedback("CLOSE", result);
    }

    private async Task DispatchSignalTradeAsync(GapSignalTriggerResult trigger)
    {
        try
        {
            if (trigger.Action == GapSignalAction.Open)
            {
                if (trigger.PrimarySide == GapSignalSide.Buy)
                {
                    await AutoBuyAsync(trigger);
                }
                else
                {
                    await AutoSellAsync(trigger);
                }
            }
            else if (trigger.Action == GapSignalAction.Close)
            {
                await AutoCloseOrderAsync(trigger);
            }
        }
        catch (Exception ex)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                SignalLogItems.Insert(0,
                    $"    - [{DateTime.Now:HH:mm:ss.fff}] Auto trade error: {ex.Message}");
            });
        }
    }

    private async Task AutoBuyAsync(GapSignalTriggerResult trigger)
    {
        var appOpenRequestTimeLocal = DateTimeOffset.Now;
        var appOpenRequestRawMs = Environment.TickCount64;

        // Capture pending request BEFORE executing click to avoid race with shared-memory polling.
        CapturePendingOpenRequestFromTrigger(TradeTab.LeftPanel.TargetMapName, trigger, isExchangeA: true, tradeType: 0, appOpenRequestTimeLocal, appOpenRequestRawMs);
        CapturePendingOpenRequestFromTrigger(TradeTab.RightPanel.TargetMapName, trigger, isExchangeA: false, tradeType: 1, appOpenRequestTimeLocal, appOpenRequestRawMs);

        var result = await _mt5ManualTradeService.ExecuteBuyAsync(
            _runtimeConfigState.CurrentChartHwndA,
            _runtimeConfigState.CurrentChartHwndB);

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            AppendAutoTradeLogs(result, trigger, "OPEN_AUTO");
        });
    }

    private async Task AutoSellAsync(GapSignalTriggerResult trigger)
    {
        var appOpenRequestTimeLocal = DateTimeOffset.Now;
        var appOpenRequestRawMs = Environment.TickCount64;

        // Capture pending request BEFORE executing click to avoid race with shared-memory polling.
        CapturePendingOpenRequestFromTrigger(TradeTab.LeftPanel.TargetMapName, trigger, isExchangeA: true, tradeType: 1, appOpenRequestTimeLocal, appOpenRequestRawMs);
        CapturePendingOpenRequestFromTrigger(TradeTab.RightPanel.TargetMapName, trigger, isExchangeA: false, tradeType: 0, appOpenRequestTimeLocal, appOpenRequestRawMs);

        var result = await _mt5ManualTradeService.ExecuteSellAsync(
            _runtimeConfigState.CurrentChartHwndA,
            _runtimeConfigState.CurrentChartHwndB);

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            AppendAutoTradeLogs(result, trigger, "OPEN_AUTO");
        });
    }

    private async Task AutoCloseOrderAsync(GapSignalTriggerResult trigger)
    {
        var selectA = SelectCloseCandidateForExchange(
            exchangeLabel: "A",
            tradeMapName: TradeTab.LeftPanel.TargetMapName,
            tradeHwnd: _runtimeConfigState.CurrentTradeHwndA);

        var selectB = SelectCloseCandidateForExchange(
            exchangeLabel: "B",
            tradeMapName: TradeTab.RightPanel.TargetMapName,
            tradeHwnd: _runtimeConfigState.CurrentTradeHwndB);

        var appCloseRequestTimeLocal = DateTimeOffset.Now;
        var appCloseRequestRawMs = Environment.TickCount64;

        // Capture pending request BEFORE executing close to avoid race with shared-memory polling.
        CapturePendingCloseRequestFromTrigger(selectA, trigger, isExchangeA: true, appCloseRequestTimeLocal, appCloseRequestRawMs);
        CapturePendingCloseRequestFromTrigger(selectB, trigger, isExchangeA: false, appCloseRequestTimeLocal, appCloseRequestRawMs);

        var result = await _mt5ManualTradeService.ExecuteCloseAsync(
            selectA.Request,
            selectB.Request);

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            AppendAutoTradeLogs(result, trigger, "CLOSE_AUTO");
            AppendCloseSelectionDiagnostics(selectA, selectB);
        });
    }

    private void AppendAutoTradeLogs(ManualTradeResult result, GapSignalTriggerResult trigger, string label)
    {
        var now = DateTime.Now;
        var lines = new List<string>
        {
            $"[{label}] Auto",
            "    = Auto trigger from signal engine"
        };

        foreach (var leg in result.Legs)
        {
            var status = leg.Success ? "OK" : "FAILED";
            var price = ResolveAutoLogPrice(trigger, leg.Exchange, leg.Action);
            var priceText = price.HasValue
                ? $" @{price.Value.ToString("0.#####", CultureInfo.InvariantCulture)}"
                : string.Empty;

            lines.Add($"    - [{now:HH:mm:ss.fff}] {leg.Action} {leg.Exchange}{priceText} {status} ({leg.Detail})");
        }

        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            lines.Add($"    = ERROR: {result.ErrorMessage}");
        }

        LastSignalText = lines[0];
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            SignalLogItems.Insert(0, lines[i]);
        }
    }

    private void CapturePendingOpenRequestFromTrigger(
        string tradeMapName,
        GapSignalTriggerResult trigger,
        bool isExchangeA,
        int tradeType,
        DateTimeOffset appOpenRequestTimeLocal,
        long appOpenRequestRawMs)
    {
        var expectedPrice = ResolveExpectedPriceFromTrigger(trigger, isExchangeA, tradeType);
        if (!expectedPrice.HasValue)
        {
            return;
        }

        RegisterPendingOpenRequest(
            tradeMapName: tradeMapName,
            symbol: null,
            volume: null,
            tradeType: tradeType,
            expectedPrice: expectedPrice.Value,
            appOpenRequestTimeLocal: appOpenRequestTimeLocal,
            appOpenRequestRawMs: appOpenRequestRawMs);
    }

    private void CapturePendingCloseRequestFromTrigger(
        CloseSelectionResult selection,
        GapSignalTriggerResult trigger,
        bool isExchangeA,
        DateTimeOffset appCloseRequestTimeLocal,
        long appCloseRequestRawMs)
    {
        if (selection.Request is null || !selection.TradeType.HasValue)
        {
            return;
        }

        var originalTradeType = selection.TradeType.Value;
        var closeTradeType = originalTradeType == 0 ? 1 : 0;
        var expectedPrice = ResolveExpectedPriceFromTrigger(trigger, isExchangeA, closeTradeType);
        if (!expectedPrice.HasValue)
        {
            return;
        }

        RegisterPendingCloseRequest(
            tradeMapName: ResolveTradeMapNameFromCloseSelection(selection),
            ticket: selection.Request.Ticket,
            tradeType: originalTradeType,
            expectedPrice: expectedPrice.Value,
            appCloseRequestTimeLocal: appCloseRequestTimeLocal,
            appCloseRequestRawMs: appCloseRequestRawMs,
            symbol: selection.Symbol,
            volume: selection.Volume);
    }

    private static double? ResolveExpectedPriceFromTrigger(GapSignalTriggerResult trigger, bool isExchangeA, int tradeType)
    {
        var isBuy = tradeType == 0;

        if (isExchangeA)
        {
            if (isBuy)
            {
                return trigger.LastAAsk.HasValue ? (double)trigger.LastAAsk.Value : null;
            }

            return trigger.LastABid.HasValue ? (double)trigger.LastABid.Value : null;
        }

        if (isBuy)
        {
            return trigger.LastBAsk.HasValue ? (double)trigger.LastBAsk.Value : null;
        }

        return trigger.LastBBid.HasValue ? (double)trigger.LastBBid.Value : null;
    }

    private static decimal? ResolveAutoLogPrice(GapSignalTriggerResult trigger, string exchange, string action)
    {
        var isExchangeA = string.Equals(exchange, "A", StringComparison.OrdinalIgnoreCase);
        var isBuy = string.Equals(action, "BUY", StringComparison.OrdinalIgnoreCase);
        var isSell = string.Equals(action, "SELL", StringComparison.OrdinalIgnoreCase);

        if (isExchangeA)
        {
            if (isBuy)
            {
                return trigger.LastAAsk;
            }

            if (isSell)
            {
                return trigger.LastABid;
            }
        }
        else
        {
            if (isBuy)
            {
                return trigger.LastBAsk;
            }

            if (isSell)
            {
                return trigger.LastBBid;
            }
        }

        return null;
    }

    private CloseSelectionResult SelectCloseCandidateForExchange(string exchangeLabel, string tradeMapName, string tradeHwnd)
    {
        var result = _tradesSharedMemoryReader.ReadTrades(tradeMapName);

        if (!result.IsMapAvailable)
        {
            return new CloseSelectionResult(
                Request: null,
                Status: CloseSelectionStatus.MapNotFound,
                TradeType: null,
                TradeMapName: tradeMapName,
                Symbol: null,
                Volume: null,
                DiagnosticMessage: $"Close {exchangeLabel} skipped: map not found ({tradeMapName})");
        }

        if (!result.IsParseSuccess)
        {
            return new CloseSelectionResult(
                Request: null,
                Status: CloseSelectionStatus.ParseError,
                TradeType: null,
                TradeMapName: tradeMapName,
                Symbol: null,
                Volume: null,
                DiagnosticMessage: $"Close {exchangeLabel} skipped: parse error ({result.ErrorMessage ?? "unknown"})");
        }

        if (result.Count <= 0 || result.Records.Count == 0)
        {
            return new CloseSelectionResult(
                Request: null,
                Status: CloseSelectionStatus.NoOpenTrade,
                TradeType: null,
                TradeMapName: tradeMapName,
                Symbol: null,
                Volume: null,
                DiagnosticMessage: null);
        }

        var firstTrade = result.Records[0];
        return new CloseSelectionResult(
            Request: new ManualCloseRequest(exchangeLabel, tradeHwnd, firstTrade.Ticket),
            Status: CloseSelectionStatus.Candidate,
            TradeType: firstTrade.TradeType,
            TradeMapName: tradeMapName,
            Symbol: firstTrade.Symbol,
            Volume: firstTrade.Lot,
            DiagnosticMessage: null);
    }

    private void AppendCloseSelectionDiagnostics(CloseSelectionResult selectionA, CloseSelectionResult selectionB)
    {
        var now = DateTime.Now;

        if (!string.IsNullOrWhiteSpace(selectionA.DiagnosticMessage))
        {
            SignalLogItems.Insert(0, $"    - [{now:HH:mm:ss.fff}] {selectionA.DiagnosticMessage}");
        }

        if (!string.IsNullOrWhiteSpace(selectionB.DiagnosticMessage))
        {
            SignalLogItems.Insert(0, $"    - [{now:HH:mm:ss.fff}] {selectionB.DiagnosticMessage}");
        }

        if (selectionA.Status == CloseSelectionStatus.NoOpenTrade &&
            selectionB.Status == CloseSelectionStatus.NoOpenTrade)
        {
            SignalLogItems.Insert(0, $"    - [{now:HH:mm:ss.fff}] Close skipped: no open trade on both A and B");
        }
    }

    private enum CloseSelectionStatus
    {
        Candidate,
        NoOpenTrade,
        MapNotFound,
        ParseError
    }

    private sealed record CloseSelectionResult(
        ManualCloseRequest? Request,
        CloseSelectionStatus Status,
        int? TradeType,
        string? TradeMapName,
        string? Symbol,
        double? Volume,
        string? DiagnosticMessage);

    private void CapturePendingOpenRequest(
        string tradeMapName,
        DashboardMetrics? snapshot,
        bool isExchangeA,
        int tradeType,
        DateTimeOffset appOpenRequestTimeLocal,
        long appOpenRequestRawMs)
    {
        var expectedPrice = ResolveExpectedOpenPrice(snapshot, isExchangeA, tradeType);
        if (!expectedPrice.HasValue)
        {
            return;
        }

        RegisterPendingOpenRequest(
            tradeMapName: tradeMapName,
            symbol: ResolveExpectedOpenSymbol(snapshot, isExchangeA),
            volume: null,
            tradeType: tradeType,
            expectedPrice: expectedPrice.Value,
            appOpenRequestTimeLocal: appOpenRequestTimeLocal,
            appOpenRequestRawMs: appOpenRequestRawMs);
    }

    private void CapturePendingCloseRequest(
        CloseSelectionResult selection,
        DashboardMetrics? snapshot,
        bool isExchangeA,
        DateTimeOffset appCloseRequestTimeLocal,
        long appCloseRequestRawMs)
    {
        if (selection.Request is null || !selection.TradeType.HasValue)
        {
            return;
        }

        var expectedPrice = ResolveExpectedClosePrice(snapshot, isExchangeA, selection.TradeType.Value);
        if (!expectedPrice.HasValue)
        {
            return;
        }

        RegisterPendingCloseRequest(
            tradeMapName: ResolveTradeMapNameFromCloseSelection(selection),
            ticket: selection.Request.Ticket,
            tradeType: selection.TradeType.Value,
            expectedPrice: expectedPrice.Value,
            appCloseRequestTimeLocal: appCloseRequestTimeLocal,
            appCloseRequestRawMs: appCloseRequestRawMs,
            symbol: selection.Symbol,
            volume: selection.Volume);
    }

    private void RegisterPendingOpenRequest(
        string tradeMapName,
        string? symbol,
        double? volume,
        int tradeType,
        double expectedPrice,
        DateTimeOffset appOpenRequestTimeLocal,
        long appOpenRequestRawMs)
    {
        var key = NormalizeMapName(tradeMapName);
        if (!_pendingOpenRequestsByMap.TryGetValue(key, out var pendingList))
        {
            pendingList = [];
            _pendingOpenRequestsByMap[key] = pendingList;
        }

        var pending = new PendingOpenRequest(
            TradeMapName: key,
            Symbol: symbol,
            TradeType: tradeType,
            Volume: volume,
            ExpectedPrice: expectedPrice,
            AppOpenRequestTimeLocal: appOpenRequestTimeLocal,
            AppOpenRequestUnixMs: appOpenRequestTimeLocal.ToUnixTimeMilliseconds(),
            AppOpenRequestRawMs: appOpenRequestRawMs);

        pendingList.Add(pending);
        Debug.WriteLine($"[ExecOpen][Capture] map={key}, type={tradeType}, app_open_request_time={pending.AppOpenRequestTimeLocal:O}, app_open_request_raw_ms={pending.AppOpenRequestRawMs}");
    }

    private void RegisterPendingCloseRequest(
        string tradeMapName,
        ulong? ticket,
        int tradeType,
        double expectedPrice,
        DateTimeOffset appCloseRequestTimeLocal,
        long appCloseRequestRawMs,
        string? symbol,
        double? volume)
    {
        var key = NormalizeMapName(tradeMapName);
        if (!_pendingCloseRequestsByMap.TryGetValue(key, out var pendingList))
        {
            pendingList = [];
            _pendingCloseRequestsByMap[key] = pendingList;
        }

        var pending = new PendingCloseRequest(
            TradeMapName: key,
            Ticket: ticket,
            Symbol: symbol,
            TradeType: tradeType,
            Volume: volume,
            ExpectedPrice: expectedPrice,
            AppCloseRequestTimeLocal: appCloseRequestTimeLocal,
            AppCloseRequestUnixMs: appCloseRequestTimeLocal.ToUnixTimeMilliseconds(),
            AppCloseRequestRawMs: appCloseRequestRawMs);

        pendingList.Add(pending);
        Debug.WriteLine($"[ExecClose][Capture] map={key}, ticket={(ticket.HasValue ? ticket.Value.ToString(CultureInfo.InvariantCulture) : "-")}, type={tradeType}, app_close_request_time={pending.AppCloseRequestTimeLocal:O}, app_close_request_raw_ms={pending.AppCloseRequestRawMs}");
    }

    private static string ResolveTradeMapNameFromCloseSelection(CloseSelectionResult selection)
        => NormalizeMapName(selection.TradeMapName);

    private static string ResolveTradeMapNameFromHistoryMap(string historyMapName)
    {
        var normalized = NormalizeMapName(historyMapName);
        return normalized.EndsWith("_History", StringComparison.OrdinalIgnoreCase)
            ? string.Concat(normalized.AsSpan(0, normalized.Length - "_History".Length), "_Trades")
            : normalized;
    }

    private static string NormalizeMapName(string? mapName)
        => string.IsNullOrWhiteSpace(mapName) ? string.Empty : mapName.Trim();

    private void AppendManualTradeLogs(ManualTradeResult result, DashboardMetrics? snapshot)
    {
        var now = DateTime.Now;
        var lines = new List<string>
        {
            $"[{result.Label}] Manual",
            "    = Manual trigger from UI buttons"
        };

        foreach (var leg in result.Legs)
        {
            var status = leg.Success ? "OK" : "FAILED";
            var price = ResolveManualLogPrice(snapshot, leg.Exchange, leg.Action);
            var priceText = price.HasValue
                ? $" @{price.Value.ToString("0.#####", CultureInfo.InvariantCulture)}"
                : string.Empty;

            lines.Add($"    - [{now:HH:mm:ss.fff}] {leg.Action} {leg.Exchange}{priceText} {status} ({leg.Detail})");
        }

        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            lines.Add($"    = ERROR: {result.ErrorMessage}");
        }

        LastSignalText = lines[0];
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            SignalLogItems.Insert(0, lines[i]);
        }
    }

    private static decimal? ResolveManualLogPrice(DashboardMetrics? snapshot, string exchange, string action)
    {
        if (snapshot is null)
        {
            return null;
        }

        var isExchangeA = string.Equals(exchange, "A", StringComparison.OrdinalIgnoreCase);
        var isExchangeB = string.Equals(exchange, "B", StringComparison.OrdinalIgnoreCase);
        var isBuy = string.Equals(action, "BUY", StringComparison.OrdinalIgnoreCase);
        var isSell = string.Equals(action, "SELL", StringComparison.OrdinalIgnoreCase);

        if (isExchangeA)
        {
            if (isBuy)
            {
                return snapshot.ExchangeA.Bid;
            }

            if (isSell)
            {
                return snapshot.ExchangeA.Ask;
            }
        }

        if (isExchangeB)
        {
            if (isBuy)
            {
                return snapshot.ExchangeB.Bid;
            }

            if (isSell)
            {
                return snapshot.ExchangeB.Ask;
            }
        }

        return null;
    }

    private static double? ResolveExpectedOpenPrice(DashboardMetrics? snapshot, bool isExchangeA, int tradeType)
    {
        if (snapshot is null)
        {
            return null;
        }

        var exchange = isExchangeA ? snapshot.ExchangeA : snapshot.ExchangeB;
        var isBuy = tradeType == 0;

        if (isBuy)
        {
            return exchange.Ask.HasValue ? (double)exchange.Ask.Value : null;
        }

        return exchange.Bid.HasValue ? (double)exchange.Bid.Value : null;
    }

    private static string? ResolveExpectedOpenSymbol(DashboardMetrics? snapshot, bool isExchangeA)
    {
        if (snapshot is null)
        {
            return null;
        }

        return isExchangeA ? snapshot.ExchangeA.Symbol : snapshot.ExchangeB.Symbol;
    }

    private static double? ResolveExpectedClosePrice(DashboardMetrics? snapshot, bool isExchangeA, int originalTradeType)
    {
        if (snapshot is null)
        {
            return null;
        }

        var exchange = isExchangeA ? snapshot.ExchangeA : snapshot.ExchangeB;
        var isBuyPosition = originalTradeType == 0;

        if (isBuyPosition)
        {
            return exchange.Bid.HasValue ? (double)exchange.Bid.Value : null;
        }

        return exchange.Ask.HasValue ? (double)exchange.Ask.Value : null;
    }

    private void ShowManualTradeFeedback(string actionName, ManualTradeResult result)
    {
        var detail = BuildManualTradeFeedbackText(actionName, result);
        var hasFailedLeg = result.Legs.Any(x => !x.Success);
        var isError = !result.Success || hasFailedLeg || !string.IsNullOrWhiteSpace(result.ErrorMessage);

        if (isError)
        {
            TryCopyToClipboard(detail);
            System.Windows.MessageBox.Show(
                detail + "\n\n(Đã copy lỗi vào clipboard)",
                $"{actionName} FAILED",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        System.Windows.MessageBox.Show(
            detail,
            $"{actionName} SUCCESS",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }

    private static string BuildManualTradeFeedbackText(string actionName, ManualTradeResult result)
    {
        var lines = new List<string>
        {
            $"Action: {actionName}",
            $"Label: {result.Label}",
            $"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}",
            $"Result: {(result.Success ? "SUCCESS" : "FAILED")}",
            "Details:"
        };

        if (result.Legs.Count == 0)
        {
            lines.Add("- (no leg details)");
        }
        else
        {
            foreach (var leg in result.Legs)
            {
                lines.Add($"- {leg.Exchange} {leg.Action}: {(leg.Success ? "OK" : "FAILED")} | {leg.Detail}");
            }
        }

        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            lines.Add($"Error: {result.ErrorMessage}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static void TryCopyToClipboard(string text)
    {
        try
        {
            System.Windows.Clipboard.SetText(text);
        }
        catch
        {
            // Ignore clipboard failures.
        }
    }

    private void ResetTradingLogicState()
    {
        _tradingFlowEngine.Reset();
        OnPropertyChanged(nameof(CurrentPositionText));
        OnPropertyChanged(nameof(CurrentPhaseText));
    }

    private Task CopyHostNameAsync()
    {
        try
        {
            var hostNameToCopy = string.IsNullOrWhiteSpace(_normalizedHostName)
                ? MachineHostName
                : _normalizedHostName;

            if (string.IsNullOrWhiteSpace(hostNameToCopy))
            {
                return Task.CompletedTask;
            }

            System.Windows.Clipboard.SetText(hostNameToCopy);
        }
        catch
        {
            // Ignore clipboard failures to keep UI responsive.
        }

        return Task.CompletedTask;
    }

    private Task OpenConfigAsync()
    {
        try
        {
            ClearConfigError();
            var configWindow = _serviceProvider.GetRequiredService<ConfigWindow>();
            var mainWindow = System.Windows.Application.Current?.MainWindow;
            if (mainWindow is not null && !ReferenceEquals(mainWindow, configWindow))
            {
                configWindow.Owner = mainWindow;
            }

            configWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            var error = $"[Config] Không mở được cửa sổ Config: {ex.Message}";
            ShowConfigError(error);

            var owner = System.Windows.Application.Current?.MainWindow;
            System.Windows.MessageBox.Show(
                owner,
                error,
                "Config Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }

        return Task.CompletedTask;
    }

    private void ShowConfigError(string message)
    {
        ConfigErrorMessage = message;
        IsConfigErrorVisible = !string.IsNullOrWhiteSpace(message);
    }

    private void ClearConfigError()
    {
        ConfigErrorMessage = string.Empty;
        IsConfigErrorVisible = false;
    }

    private async Task ReconnectConfigAsync()
    {
        await InitializeRuntimeConfigAsync();
    }

    private void ApplyRuntimeConfig()
    {
        ExchangeAHeader = string.IsNullOrWhiteSpace(_runtimeConfigState.MapName1)
            ? "Sàn A"
            : $"Sàn A ({_runtimeConfigState.MapName1})";

        ExchangeBHeader = string.IsNullOrWhiteSpace(_runtimeConfigState.MapName2)
            ? "Sàn B"
            : $"Sàn B ({_runtimeConfigState.MapName2})";

        RuntimeSummary =
            $"Host Name: {_runtimeConfigState.CurrentMachineHostName}  |  Point: {_runtimeConfigState.CurrentPoint}  |  OpenPts: {_runtimeConfigState.CurrentOpenPts}  |  ConfirmGapPts: {_runtimeConfigState.CurrentConfirmGapPts}  |  HoldConfirmMs: {_runtimeConfigState.CurrentHoldConfirmMs}  |  ClosePts: {_runtimeConfigState.CurrentClosePts}  |  CloseConfirmGapPts: {_runtimeConfigState.CurrentCloseConfirmGapPts}  |  CloseHoldConfirmMs: {_runtimeConfigState.CurrentCloseHoldConfirmMs}  |  StartTimeHold: {_runtimeConfigState.CurrentStartTimeHold}  |  EndTimeHold: {_runtimeConfigState.CurrentEndTimeHold}  |  StartWaitTime: {_runtimeConfigState.CurrentStartWaitTime}  |  EndWaitTime: {_runtimeConfigState.CurrentEndWaitTime}  |  Map 1: {_runtimeConfigState.CurrentMapName1}  |  Map 2: {_runtimeConfigState.CurrentMapName2}";

        HasManualTradeHwndConfig =
            !string.IsNullOrWhiteSpace(_runtimeConfigState.CurrentChartHwndA) &&
            !string.IsNullOrWhiteSpace(_runtimeConfigState.CurrentTradeHwndA) &&
            !string.IsNullOrWhiteSpace(_runtimeConfigState.CurrentChartHwndB) &&
            !string.IsNullOrWhiteSpace(_runtimeConfigState.CurrentTradeHwndB);

        RefreshOrderInfoTabs();
    }

    private void RefreshOrderInfoTabs()
    {
        var tickMapA = _runtimeConfigState.MapName1;
        var tickMapB = _runtimeConfigState.MapName2;

        BindTabMapNames(
            TradeTab,
            tickMapA,
            tickMapB,
            OrderMapNameResolver.BuildTradeMapName);

        BindTabMapNames(
            HistoryTab,
            tickMapA,
            tickMapB,
            OrderMapNameResolver.BuildHistoryMapName);
    }

    private static void BindTabMapNames(
        OrderInfoTabViewModel tab,
        string leftTickMap,
        string rightTickMap,
        Func<string, string> mapNameResolver)
    {
        BindPanelMapName(tab.LeftPanel, leftTickMap, mapNameResolver);
        BindPanelMapName(tab.RightPanel, rightTickMap, mapNameResolver);
    }

    private static void BindPanelMapName(
        OrderPanelStatusViewModel panel,
        string sourceTickMapName,
        Func<string, string> mapNameResolver)
    {
        var targetMapName = mapNameResolver(sourceTickMapName);

        if (string.Equals(panel.TargetMapName, targetMapName, StringComparison.Ordinal))
        {
            return;
        }

        panel.ApplyMapBinding(sourceTickMapName, targetMapName);
        panel.SetLoading();
    }

    private async Task RunOrderInfoPollingAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(OrderInfoPollInterval);

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var tradeLeftResult = _tradesSharedMemoryReader.ReadTrades(TradeTab.LeftPanel.TargetMapName);
            var tradeRightResult = _tradesSharedMemoryReader.ReadTrades(TradeTab.RightPanel.TargetMapName);
            var historyLeftResult = _historySharedMemoryReader.ReadHistory(HistoryTab.LeftPanel.TargetMapName);
            var historyRightResult = _historySharedMemoryReader.ReadHistory(HistoryTab.RightPanel.TargetMapName);
            var shouldApplyTradeLeft = ShouldApplyTradeResult(TradeTab.LeftPanel.TargetMapName, tradeLeftResult);
            var shouldApplyTradeRight = ShouldApplyTradeResult(TradeTab.RightPanel.TargetMapName, tradeRightResult);
            var shouldApplyHistoryLeft = ShouldApplyHistoryResult(HistoryTab.LeftPanel.TargetMapName, historyLeftResult);
            var shouldApplyHistoryRight = ShouldApplyHistoryResult(HistoryTab.RightPanel.TargetMapName, historyRightResult);
            var snapshot = _runtimeConfigState.CurrentDashboardMetrics;
            var point = _runtimeConfigState.CurrentPoint;

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (shouldApplyTradeLeft)
                {
                    _latestTradeLeftResult = tradeLeftResult;
                    ApplyTradeResult(TradeTab.LeftPanel, tradeLeftResult, snapshot, isExchangeA: true, point);
                }

                if (shouldApplyTradeRight)
                {
                    _latestTradeRightResult = tradeRightResult;
                    ApplyTradeResult(TradeTab.RightPanel, tradeRightResult, snapshot, isExchangeA: false, point);
                }

                if (shouldApplyHistoryLeft)
                {
                    ApplyHistoryResult(HistoryTab.LeftPanel, historyLeftResult, point);
                }

                if (shouldApplyHistoryRight)
                {
                    ApplyHistoryResult(HistoryTab.RightPanel, historyRightResult, point);
                }
            });
        }
    }

    private bool ShouldApplyTradeResult(string mapName, SharedMapReadResult<TradeSharedRecord> result)
    {
        if (!result.IsMapAvailable || !result.IsParseSuccess)
        {
            _lastTradeTimestampByMap.Remove(mapName ?? string.Empty);
            return true;
        }

        var key = mapName ?? string.Empty;
        if (_lastTradeTimestampByMap.TryGetValue(key, out var lastTimestamp) && lastTimestamp == result.Timestamp)
        {
            return false;
        }

        _lastTradeTimestampByMap[key] = result.Timestamp;
        return true;
    }

    private bool ShouldApplyHistoryResult(string mapName, SharedMapReadResult<HistorySharedRecord> result)
    {
        if (!result.IsMapAvailable || !result.IsParseSuccess)
        {
            _lastHistoryTimestampByMap.Remove(mapName ?? string.Empty);
            return true;
        }

        var key = mapName ?? string.Empty;
        if (_lastHistoryTimestampByMap.TryGetValue(key, out var lastTimestamp) && lastTimestamp == result.Timestamp)
        {
            return false;
        }

        _lastHistoryTimestampByMap[key] = result.Timestamp;
        return true;
    }

    private void ApplyTradeResult(
        OrderPanelStatusViewModel panel,
        SharedMapReadResult<TradeSharedRecord> result,
        DashboardMetrics? snapshot,
        bool isExchangeA,
        int point)
    {
        if (!result.IsMapAvailable)
        {
            panel.SetMapNotFound(panel.TargetMapName);
            return;
        }

        if (!result.IsParseSuccess)
        {
            panel.SetParseError(result.ErrorMessage ?? "Lỗi parse dữ liệu");
            return;
        }

        if (result.Count == 0)
        {
            panel.SetEmpty();
            return;
        }

        if (result.Records.Count == 0)
        {
            panel.SetParseError("Lỗi parse dữ liệu: count > 0 nhưng không có records");
            return;
        }

        RegisterOpenExpectedForNewTickets(panel.TargetMapName, result.Records);
        var rows = BuildTradeRows(result.Records, result.Count, result.Timestamp, snapshot, isExchangeA, point);
        panel.SetTradeData(rows);
    }

    private void ApplyHistoryResult(
        OrderPanelStatusViewModel panel,
        SharedMapReadResult<HistorySharedRecord> result,
        int point)
    {
        if (!result.IsMapAvailable)
        {
            panel.SetMapNotFound(panel.TargetMapName);
            return;
        }

        if (!result.IsParseSuccess)
        {
            panel.SetParseError(result.ErrorMessage ?? "Lỗi parse dữ liệu");
            return;
        }

        if (result.Count == 0)
        {
            panel.SetEmpty();
            return;
        }

        if (result.Records.Count == 0)
        {
            panel.SetParseError("Lỗi parse dữ liệu: count > 0 nhưng không có records");
            return;
        }

        RegisterCloseExecutionForNewHistoryTickets(panel.TargetMapName, result.Records);
        var rows = BuildHistoryRows(result.Records, result.Count, result.Timestamp, point);
        panel.SetHistoryData(rows);
    }

    private void RegisterOpenExpectedForNewTickets(string tradeMapName, IReadOnlyList<TradeSharedRecord> records)
    {
        var key = NormalizeMapName(tradeMapName);
        if (!_knownTradeTicketsByMap.TryGetValue(key, out var knownTickets))
        {
            knownTickets = [];
            _knownTradeTicketsByMap[key] = knownTickets;
        }

        var currentTickets = records.Select(r => r.Ticket).ToHashSet();
        var newRecords = records.Where(r => !knownTickets.Contains(r.Ticket)).OrderBy(r => r.TimeMsc).ToList();

        foreach (var newRecord in newRecords)
        {
            if (!TryConsumePendingOpenRequest(key, newRecord, out var pendingRequest, out var matchKey))
            {
                continue;
            }

            _openRequestByTicket[newRecord.Ticket] = pendingRequest;
            var openExecutionMs = ComputeExecutionMilliseconds(
                newRecord.OpenEaTimeLocal,
                pendingRequest.AppOpenRequestRawMs);
            if (openExecutionMs.HasValue)
            {
                _openExecutionMsByTicket[newRecord.Ticket] = openExecutionMs.Value;
            }

            Debug.WriteLine(
                $"[ExecOpen][Raw] key={matchKey}, ticket={newRecord.Ticket}, app_open_request_time_raw={pendingRequest.AppOpenRequestTimeLocal:O}, app_open_request_raw_ms={pendingRequest.AppOpenRequestRawMs}, " +
                $"open_ea_time_local_raw={newRecord.OpenEaTimeLocal}");

            Debug.WriteLine(
                $"[ExecOpen][Match] key={matchKey}, ticket={newRecord.Ticket}, app_open_request_time={pendingRequest.AppOpenRequestTimeLocal:O}, " +
                $"open_ea_time_local={newRecord.OpenEaTimeLocal}, app_open_request_raw_ms={pendingRequest.AppOpenRequestRawMs}, " +
                $"open_execution={(openExecutionMs.HasValue ? openExecutionMs.Value.ToString(CultureInfo.InvariantCulture) : "--")}");
        }

        _knownTradeTicketsByMap[key] = currentTickets;
    }

    private void RegisterCloseExecutionForNewHistoryTickets(string historyMapName, IReadOnlyList<HistorySharedRecord> records)
    {
        var key = NormalizeMapName(historyMapName);
        var tradeMapName = ResolveTradeMapNameFromHistoryMap(historyMapName);

        if (!_knownHistoryTicketsByMap.TryGetValue(key, out var knownTickets))
        {
            knownTickets = [];
            _knownHistoryTicketsByMap[key] = knownTickets;
        }

        var currentTickets = records.Select(r => r.Ticket).ToHashSet();
        var newRecords = records
            .Where(r => !knownTickets.Contains(r.Ticket))
            .OrderBy(r => r.CloseTimeMsc)
            .ToList();

        foreach (var record in newRecords)
        {
            if (_closeExecutionMsByTicket.ContainsKey(record.Ticket))
            {
                continue;
            }

            if (!TryConsumePendingCloseRequest(tradeMapName, record, out var pendingRequest, out var matchKey))
            {
                continue;
            }

            _closeRequestByTicket[record.Ticket] = pendingRequest;
            var closeExecutionMs = ComputeExecutionMilliseconds(
                record.CloseEaTimeLocal,
                pendingRequest.AppCloseRequestRawMs);
            if (closeExecutionMs.HasValue)
            {
                _closeExecutionMsByTicket[record.Ticket] = closeExecutionMs.Value;
            }

            Debug.WriteLine(
                $"[ExecClose][Raw] key={matchKey}, ticket={record.Ticket}, app_close_request_time_raw={pendingRequest.AppCloseRequestTimeLocal:O}, app_close_request_raw_ms={pendingRequest.AppCloseRequestRawMs}, " +
                $"close_ea_time_local_raw={record.CloseEaTimeLocal}");

            Debug.WriteLine(
                $"[ExecClose][Match] key={matchKey}, ticket={record.Ticket}, app_close_request_time={pendingRequest.AppCloseRequestTimeLocal:O}, " +
                $"close_ea_time_local={record.CloseEaTimeLocal}, app_close_request_raw_ms={pendingRequest.AppCloseRequestRawMs}, " +
                $"close_execution={(closeExecutionMs.HasValue ? closeExecutionMs.Value.ToString(CultureInfo.InvariantCulture) : "--")}");
        }

        _knownHistoryTicketsByMap[key] = currentTickets;
    }

    private IEnumerable<TradeRowViewModel> BuildTradeRows(
        IReadOnlyList<TradeSharedRecord> records,
        int count,
        ulong timestamp,
        DashboardMetrics? snapshot,
        bool isExchangeA,
        int point)
        => records.Select(record =>
        {
            _openExecutionMsByTicket.TryGetValue(record.Ticket, out var openExecutionMsValue);
            var openExecutionMs = _openExecutionMsByTicket.ContainsKey(record.Ticket) ? openExecutionMsValue : (long?)null;
            _openRequestByTicket.TryGetValue(record.Ticket, out var openRequest);
            var tradeOpenSlippage = CalculateTradeOpenSlippage(record, point);

            return new TradeRowViewModel(
                timestamp: FormatRawTimestamp(timestamp),
                count: count.ToString(CultureInfo.InvariantCulture),
                symbol: record.Symbol,
                ticket: record.Ticket.ToString(CultureInfo.InvariantCulture),
                type: FormatTradeType(record.TradeType),
                lot: FormatLot(record.Lot),
                price: FormatPrice(record.Price),
                sl: FormatPrice(record.Sl),
                tp: FormatPrice(record.Tp),
                slippage: FormatTradeOpenSlippageDebug(record, openRequest, point, tradeOpenSlippage),
                profit: FormatProfit(CalculateTradeProfit(record, snapshot, isExchangeA, point)),
                feeSpread: FormatProfit(record.Profit),
                time: FormatTradeTime(record.TimeMsc),
                openEaTimeLocal: FormatEaLocalTime(record.OpenEaTimeLocal),
                openExecution: FormatOpenExecutionDebug(record.OpenEaTimeLocal, openRequest?.AppOpenRequestRawMs, openExecutionMs));
        });

    private IEnumerable<HistoryRowViewModel> BuildHistoryRows(
        IReadOnlyList<HistorySharedRecord> records,
        int count,
        ulong timestamp,
        int point)
        => records.Select(record =>
        {
            _openExecutionMsByTicket.TryGetValue(record.Ticket, out var openExecutionMsValue);
            _closeExecutionMsByTicket.TryGetValue(record.Ticket, out var closeExecutionMsValue);
            var openExecutionMs = _openExecutionMsByTicket.ContainsKey(record.Ticket) ? openExecutionMsValue : (long?)null;
            var closeExecutionMs = _closeExecutionMsByTicket.ContainsKey(record.Ticket) ? closeExecutionMsValue : (long?)null;
            _closeRequestByTicket.TryGetValue(record.Ticket, out var closeRequest);
            var historyCloseSlippage = CalculateHistoryCloseSlippage(record, point);

            return new HistoryRowViewModel(
                timestamp: FormatRawTimestamp(timestamp),
                count: count.ToString(CultureInfo.InvariantCulture),
                symbol: record.Symbol,
                ticket: record.Ticket.ToString(CultureInfo.InvariantCulture),
                type: FormatTradeType(record.TradeType),
                volume: FormatRawDouble(record.Volume),
                openPrice: FormatRawDouble(record.OpenPrice),
                closePrice: FormatRawDouble(record.ClosePrice),
                openSlippage: FormatOptionalProfit(CalculateHistoryOpenSlippage(record, point)),
                closeSlippage: FormatHistoryCloseSlippageDebug(record, closeRequest, point, historyCloseSlippage),
                profit: FormatRawDouble(CalculateHistoryProfit(record)),
                feeSpread: FormatRawDouble(record.Profit),
                commission: FormatRawDouble(record.Commission),
                sl: FormatRawDouble(record.Sl),
                tp: FormatRawDouble(record.Tp),
                openTime: FormatTradeTime(record.OpenTimeMsc),
                closeTime: FormatTradeTime(record.CloseTimeMsc),
                closeEaTimeLocal: FormatEaLocalTime(record.CloseEaTimeLocal),
                openExecution: FormatExecutionMs(openExecutionMs),
                closeExecution: FormatCloseExecutionDebug(record.CloseEaTimeLocal, closeRequest?.AppCloseRequestRawMs, closeExecutionMs));
        });

    private static string FormatOpenExecutionDebug(ulong openEaTimeLocal, long? appOpenRequestRawMs, long? openExecutionMs)
    {
        var result = FormatExecutionMs(openExecutionMs);
        if (!appOpenRequestRawMs.HasValue || !openExecutionMs.HasValue)
        {
            return result;
        }

        return $"(open_ea_time_local - app_open_raw / {openEaTimeLocal} - {appOpenRequestRawMs.Value}) {result}";
    }

    private static string FormatCloseExecutionDebug(ulong closeEaTimeLocal, long? appCloseRequestRawMs, long? closeExecutionMs)
    {
        var result = FormatExecutionMs(closeExecutionMs);
        if (!appCloseRequestRawMs.HasValue || !closeExecutionMs.HasValue)
        {
            return result;
        }

        return $"(close_ea_time_local - app_close_raw / {closeEaTimeLocal} - {appCloseRequestRawMs.Value}) {result}";
    }

    private static string FormatTradeOpenSlippageDebug(TradeSharedRecord record, PendingOpenRequest? openRequest, int point, double? slippage)
    {
        var result = FormatOptionalProfit(slippage);
        if (openRequest is null || !slippage.HasValue)
        {
            return result;
        }

        var pointValue = Math.Max(1, point).ToString(CultureInfo.InvariantCulture);
        var expected = openRequest.ExpectedPrice.ToString("0.#####", CultureInfo.InvariantCulture);
        var openPrice = record.Price.ToString("0.#####", CultureInfo.InvariantCulture);

        return record.TradeType == 0
            ? $"((expected_open_price - open_price) * point / ({expected} - {openPrice}) * {pointValue}) {result}"
            : $"((open_price - expected_open_price) * point / ({openPrice} - {expected}) * {pointValue}) {result}";
    }

    private static string FormatHistoryCloseSlippageDebug(HistorySharedRecord record, PendingCloseRequest? closeRequest, int point, double? slippage)
    {
        var result = FormatOptionalProfit(slippage);
        if (closeRequest is null || !slippage.HasValue)
        {
            return result;
        }

        var pointValue = Math.Max(1, point).ToString(CultureInfo.InvariantCulture);
        var expected = closeRequest.ExpectedPrice.ToString("0.#####", CultureInfo.InvariantCulture);
        var closePrice = record.ClosePrice.ToString("0.#####", CultureInfo.InvariantCulture);

        return record.TradeType == 0
            ? $"((close_price - expected_close_price) * point / ({closePrice} - {expected}) * {pointValue}) {result}"
            : $"((expected_close_price - close_price) * point / ({expected} - {closePrice}) * {pointValue}) {result}";
    }

    private bool TryConsumePendingOpenRequest(
        string tradeMapName,
        TradeSharedRecord tradeRecord,
        out PendingOpenRequest pendingRequest,
        out string matchKey)
    {
        pendingRequest = default!;
        matchKey = string.Empty;

        if (!_pendingOpenRequestsByMap.TryGetValue(tradeMapName, out var pendingList) || pendingList.Count == 0)
        {
            return false;
        }

        PruneStalePendingOpenRequests(pendingList);
        if (pendingList.Count == 0)
        {
            _pendingOpenRequestsByMap.Remove(tradeMapName);
            return false;
        }

        var candidates = pendingList
            .Where(x => x.TradeType == tradeRecord.TradeType)
            .Where(x => IsNullOrMatch(x.Symbol, tradeRecord.Symbol))
            .Where(x => IsNullOrVolumeMatch(x.Volume, tradeRecord.Lot))
            .ToList();

        if (candidates.Count == 0)
        {
            Debug.WriteLine(
                $"[ExecOpen][Reject] map={tradeMapName}, ticket={tradeRecord.Ticket}, symbol={tradeRecord.Symbol}, type={tradeRecord.TradeType}, volume={tradeRecord.Lot.ToString(CultureInfo.InvariantCulture)}, reason=no_matching_pending_by_keys");
            return false;
        }

        var selected = candidates
            .OrderBy(x => Math.Abs(tradeRecord.TimeMsc > long.MaxValue
                ? long.MaxValue - x.AppOpenRequestUnixMs
                : (long)tradeRecord.TimeMsc - x.AppOpenRequestUnixMs))
            .FirstOrDefault();

        if (selected is null)
        {
            return false;
        }

        pendingList.Remove(selected);
        if (pendingList.Count == 0)
        {
            _pendingOpenRequestsByMap.Remove(tradeMapName);
        }

        pendingRequest = selected;
        matchKey = $"map={tradeMapName};symbol={tradeRecord.Symbol};type={tradeRecord.TradeType};volume={tradeRecord.Lot.ToString(CultureInfo.InvariantCulture)};mode=fallback";
        return true;
    }

    private bool TryConsumePendingCloseRequest(
        string tradeMapName,
        HistorySharedRecord historyRecord,
        out PendingCloseRequest pendingRequest,
        out string matchKey)
    {
        pendingRequest = default!;
        matchKey = string.Empty;

        if (!_pendingCloseRequestsByMap.TryGetValue(tradeMapName, out var pendingList) || pendingList.Count == 0)
        {
            return false;
        }

        PruneStalePendingCloseRequests(pendingList);
        if (pendingList.Count == 0)
        {
            _pendingCloseRequestsByMap.Remove(tradeMapName);
            return false;
        }

        var ticketMatch = pendingList.FirstOrDefault(x => x.Ticket.HasValue && x.Ticket.Value == historyRecord.Ticket);
        if (ticketMatch is not null)
        {
            pendingList.Remove(ticketMatch);
            if (pendingList.Count == 0)
            {
                _pendingCloseRequestsByMap.Remove(tradeMapName);
            }

            pendingRequest = ticketMatch;
            matchKey = $"ticket={historyRecord.Ticket};map={tradeMapName};mode=ticket";
            return true;
        }

        var candidates = pendingList
            .Where(x => x.TradeType == historyRecord.TradeType)
            .Where(x => IsNullOrMatch(x.Symbol, historyRecord.Symbol))
            .Where(x => IsNullOrVolumeMatch(x.Volume, historyRecord.Volume))
            .ToList();

        if (candidates.Count == 0)
        {
            Debug.WriteLine(
                $"[ExecClose][Reject] map={tradeMapName}, ticket={historyRecord.Ticket}, symbol={historyRecord.Symbol}, type={historyRecord.TradeType}, volume={historyRecord.Volume.ToString(CultureInfo.InvariantCulture)}, reason=no_matching_pending_by_keys");
            return false;
        }

        var selected = candidates
            .OrderBy(x => Math.Abs(historyRecord.CloseTimeMsc > long.MaxValue
                ? long.MaxValue - x.AppCloseRequestUnixMs
                : (long)historyRecord.CloseTimeMsc - x.AppCloseRequestUnixMs))
            .FirstOrDefault();

        if (selected is null)
        {
            return false;
        }

        pendingList.Remove(selected);
        if (pendingList.Count == 0)
        {
            _pendingCloseRequestsByMap.Remove(tradeMapName);
        }

        pendingRequest = selected;
        matchKey = $"map={tradeMapName};symbol={historyRecord.Symbol};type={historyRecord.TradeType};volume={historyRecord.Volume.ToString(CultureInfo.InvariantCulture)};mode=fallback";
        return true;
    }

    private static long? ComputeExecutionMilliseconds(
        ulong eaTimeLocalMs,
        long appRequestRawMs)
    {
        if (eaTimeLocalMs == 0)
        {
            return null;
        }

        var eaRawMs = eaTimeLocalMs > long.MaxValue ? long.MaxValue : (long)eaTimeLocalMs;
        return eaRawMs - appRequestRawMs;
    }

    private static bool IsNullOrMatch(string? pending, string? actual)
        => string.IsNullOrWhiteSpace(pending)
           || (!string.IsNullOrWhiteSpace(actual)
               && string.Equals(pending.Trim(), actual.Trim(), StringComparison.OrdinalIgnoreCase));

    private static bool IsNullOrVolumeMatch(double? pendingVolume, double actualVolume)
        => !pendingVolume.HasValue || Math.Abs(pendingVolume.Value - actualVolume) < 0.0000001d;

    private static void PruneStalePendingOpenRequests(List<PendingOpenRequest> pendingList)
    {
        var now = DateTimeOffset.Now;
        pendingList.RemoveAll(x => now - x.AppOpenRequestTimeLocal > TimeSpan.FromMinutes(5));
    }

    private static void PruneStalePendingCloseRequests(List<PendingCloseRequest> pendingList)
    {
        var now = DateTimeOffset.Now;
        pendingList.RemoveAll(x => now - x.AppCloseRequestTimeLocal > TimeSpan.FromMinutes(5));
    }

    private double? CalculateTradeOpenSlippage(TradeSharedRecord record, int point)
    {
        if (!_openRequestByTicket.TryGetValue(record.Ticket, out var expected) || expected.TradeType != record.TradeType)
        {
            return null;
        }

        var pointValue = Math.Max(1, point);
        var slippage = record.TradeType == 0
            ? (expected.ExpectedPrice - record.Price) * pointValue
            : (record.Price - expected.ExpectedPrice) * pointValue;

        _openSlippageByTicket[record.Ticket] = slippage;
        return slippage;
    }

    private double? CalculateHistoryOpenSlippage(HistorySharedRecord record, int point)
    {
        if (!_openSlippageByTicket.TryGetValue(record.Ticket, out var openSlippage))
        {
            return null;
        }

        return openSlippage;
    }

    private double? CalculateHistoryCloseSlippage(HistorySharedRecord record, int point)
    {
        if (!_closeRequestByTicket.TryGetValue(record.Ticket, out var expected) || expected.TradeType != record.TradeType)
        {
            return null;
        }

        var pointValue = Math.Max(1, point);
        return record.TradeType == 0
            ? (record.ClosePrice - expected.ExpectedPrice) * pointValue
            : (expected.ExpectedPrice - record.ClosePrice) * pointValue;
    }

    private static double CalculateTradeProfit(
        TradeSharedRecord record,
        DashboardMetrics? snapshot,
        bool isExchangeA,
        int point)
    {
        if (snapshot is null)
        {
            return 0d;
        }

        var exchange = isExchangeA ? snapshot.ExchangeA : snapshot.ExchangeB;
        var openPrice = (decimal)record.Price;
        var pointValue = Math.Max(1, point);
        var isBuy = record.TradeType == 0;

        if (isBuy)
        {
            if (!exchange.Bid.HasValue)
            {
                return 0d;
            }

            return (double)((exchange.Bid.Value - openPrice) * pointValue);
        }

        if (!exchange.Ask.HasValue)
        {
            return 0d;
        }

        return (double)((openPrice - exchange.Ask.Value) * pointValue);
    }

    private static double CalculateHistoryProfit(HistorySharedRecord record)
        => record.TradeType == 0
            ? (record.ClosePrice - record.OpenPrice) * 100d
            : (record.OpenPrice - record.ClosePrice) * 100d;

    private static string FormatTradeType(int tradeType)
        => tradeType == 0 ? "BUY" : "SELL";

    private static string FormatRawTimestamp(ulong timestamp)
        => timestamp.ToString(CultureInfo.InvariantCulture);

    private static string FormatRawDouble(double value)
        => value.ToString("0.#####", CultureInfo.InvariantCulture);

    private static string FormatLot(double value)
        => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatPrice(double value)
        => value.ToString("0.00000", CultureInfo.InvariantCulture);

    private static string FormatProfit(double value)
        => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatOptionalProfit(double? value)
        => value.HasValue
            ? value.Value.ToString("0.00", CultureInfo.InvariantCulture)
            : "-";

    private static string FormatExecutionMs(long? value)
        => value.HasValue
            ? $"{value.Value.ToString(CultureInfo.InvariantCulture)} ms"
            : "--";

    private static string FormatTradeTime(ulong timeMsc)
    {
        if (timeMsc == 0)
        {
            return "-";
        }

        try
        {
            var clamped = timeMsc > long.MaxValue ? long.MaxValue : (long)timeMsc;
            return DateTimeOffset.FromUnixTimeMilliseconds(clamped).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }
        catch
        {
            return FormatRawTimestamp(timeMsc);
        }
    }

    private static string FormatEaLocalTime(ulong timeMsc)
    {
        if (timeMsc == 0)
        {
            return "-";
        }

        try
        {
            var clamped = timeMsc > long.MaxValue ? long.MaxValue : (long)timeMsc;
            var time = TimeSpan.FromMilliseconds(clamped);
            return $"{time.Hours:00}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}";
        }
        catch
        {
            return FormatRawTimestamp(timeMsc);
        }
    }

    private async Task InitializeRuntimeConfigAsync()
    {
        const string InlineDbHostName = "win-vps-01";
        try
        {
            LoadingMessage = "Đang tải cấu hình runtime...";

            var result = await _configService.LoadByMachineHostNameAsync();
            if (result.IsSuccess && result.Exists)
            {
                ClearConfigError();
                _runtimeConfigState.Update(
                    result.MachineHostName,
                    result.MapName1,
                    result.MapName2,
                    result.Point,
                    result.OpenPts,
                    result.ConfirmGapPts,
                    result.HoldConfirmMs,
                    result.ClosePts,
                    result.CloseConfirmGapPts,
                    result.CloseHoldConfirmMs,
                    result.StartTimeHold,
                    result.EndTimeHold,
                    result.StartWaitTime,
                    result.EndWaitTime);
                ResetTradingLogicState();

                if (string.Equals(result.MachineHostName, InlineDbHostName, StringComparison.OrdinalIgnoreCase))
                {
                    DbInlineData =
                        $"[DB] id={result.ConfigId} | hostname={result.MachineHostName} | point={result.Point} | open_pts={result.OpenPts} | open_confirm_gap_pts={result.ConfirmGapPts} | open_hold_confirm_ms={result.HoldConfirmMs} | close_pts={result.ClosePts} | close_confirm_gap_pts={result.CloseConfirmGapPts} | close_hold_confirm_ms={result.CloseHoldConfirmMs} | start_time_hold={result.StartTimeHold} | end_time_hold={result.EndTimeHold} | start_wait_time={result.StartWaitTime} | end_wait_time={result.EndWaitTime} | sans={result.SansJson}";
                    IsDbInlineDataVisible = true;
                }
                else
                {
                    DbInlineData = string.Empty;
                    IsDbInlineDataVisible = false;
                }

                LoadingMessage = "Đang chờ dữ liệu shared memory...";
                return;
            }

            if (string.Equals(result.MachineHostName, InlineDbHostName, StringComparison.OrdinalIgnoreCase))
            {
                DbInlineData = "[DB] Không lấy được dữ liệu config từ DB cho hostname win-vps-01";
                IsDbInlineDataVisible = true;
            }
            else
            {
                DbInlineData = string.Empty;
                IsDbInlineDataVisible = false;
            }

            var warning = string.IsNullOrWhiteSpace(result.MachineHostName)
                ? "[Config] Không lấy được host name local để tải config."
                : $"[Config] Không tìm thấy config cho host name hiện tại: {result.MachineHostName}";

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                ShowConfigError(warning);
                if (!string.IsNullOrWhiteSpace(result.MachineHostName))
                {
                    _runtimeConfigState.Update(
                        result.MachineHostName,
                        _runtimeConfigState.MapName1,
                        _runtimeConfigState.MapName2,
                        _runtimeConfigState.CurrentPoint);
                }

                LoadingMessage = "Đang chờ dữ liệu shared memory...";
            });
        }
        catch (Exception ex)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                ShowConfigError($"[Config] Lỗi tải config runtime: {ex.Message}");
                LoadingMessage = "Đang chờ dữ liệu shared memory...";
            });
        }
    }

    private async Task StartExchangeReaderSafeAsync(IExchangePairReader exchangePairReader)
    {
        try
        {
            await exchangePairReader.StartAsync();
        }
        catch (Exception ex)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                ShowConfigError($"[Reader] Không thể start shared memory reader: {ex.Message}");
                LoadingMessage = "Không thể kết nối shared memory. Mở Config hoặc kiểm tra log.";
            });
        }
    }

    private void OnSnapshotReceived(object? sender, SharedMemorySnapshot snapshot)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var metrics = _dashboardMetricsMapper.Map(snapshot);
            _runtimeConfigState.UpdateDashboardMetrics(metrics);
            IsLoading = false;

            BindDashboardMetrics(metrics);
            RefreshTradeRowsFromSnapshot(metrics, _runtimeConfigState.CurrentPoint);

            if (!IsTradingLogicEnabled)
            {
                return;
            }

            var trigger = _tradingFlowEngine.ProcessSnapshot(
                new GapSignalSnapshot(
                    metrics.TimestampUtc,
                    metrics.ExchangeA.Bid,
                    metrics.ExchangeA.Ask,
                    metrics.ExchangeB.Bid,
                    metrics.ExchangeB.Ask,
                    metrics.GapBuy,
                    metrics.GapSell,
                    _runtimeConfigState.CurrentPoint),
                new GapSignalConfirmationConfig(
                    ConfirmGapPts: _runtimeConfigState.CurrentConfirmGapPts,
                    OpenPts: _runtimeConfigState.CurrentOpenPts,
                    HoldConfirmMs: _runtimeConfigState.CurrentHoldConfirmMs,
                    CloseConfirmGapPts: _runtimeConfigState.CurrentCloseConfirmGapPts,
                    ClosePts: _runtimeConfigState.CurrentClosePts,
                    CloseHoldConfirmMs: _runtimeConfigState.CurrentCloseHoldConfirmMs,
                    StartTimeHold: _runtimeConfigState.CurrentStartTimeHold,
                    EndTimeHold: _runtimeConfigState.CurrentEndTimeHold,
                    StartWaitTime: _runtimeConfigState.CurrentStartWaitTime,
                    EndWaitTime: _runtimeConfigState.CurrentEndWaitTime));

            OnPropertyChanged(nameof(CurrentPositionText));
            OnPropertyChanged(nameof(CurrentPhaseText));

            if (trigger is null || !trigger.Triggered)
            {
                return;
            }

            var instruction = _tradeInstructionFactory.Create(trigger);
            var signalLines = _tradeSignalLogBuilder.BuildLogLines(instruction);
            LastSignalText = signalLines.Count > 0 ? signalLines[0] : "-";
            for (var i = signalLines.Count - 1; i >= 0; i--)
            {
                SignalLogItems.Insert(0, signalLines[i]);
            }

            var triggeredAtLocal = trigger.TriggeredAtUtc.ToLocalTime();

            if (trigger.Action == GapSignalAction.Open)
            {
                var holdingSeconds = _tradingFlowEngine.CurrentHoldingSeconds;
                var holdText = $"[{triggeredAtLocal:HH:mm:ss.fff}] Random holding time {holdingSeconds}s";
                SignalLogItems.Insert(0, holdText);
            }
            else
            {
                var waitSeconds = _tradingFlowEngine.CurrentWaitSeconds;
                var waitText = $"[{triggeredAtLocal:HH:mm:ss.fff}] Random waiting time {waitSeconds}s";
                SignalLogItems.Insert(0, waitText);
            }

            // Auto-execute trade from signal trigger
            _ = DispatchSignalTradeAsync(trigger);
        });
    }

    private void RefreshTradeRowsFromSnapshot(DashboardMetrics metrics, int point)
    {
        if (_latestTradeLeftResult is not null)
        {
            ApplyTradeResult(
                TradeTab.LeftPanel,
                _latestTradeLeftResult,
                metrics,
                isExchangeA: true,
                point);
        }

        if (_latestTradeRightResult is not null)
        {
            ApplyTradeResult(
                TradeTab.RightPanel,
                _latestTradeRightResult,
                metrics,
                isExchangeA: false,
                point);
        }
    }

    private string ResolveCurrentPositionText()
    {
        if (!IsTradingLogicEnabled)
        {
            return "NONE";
        }

        return _tradingFlowEngine.CurrentPositionSide switch
        {
            TradingPositionSide.Buy => "BUY",
            TradingPositionSide.Sell => "SELL",
            _ => "NONE"
        };
    }

    private string ResolveCurrentPhaseText()
    {
        var phase = _tradingFlowEngine.CurrentPhase;
        return phase switch
        {
            TradingFlowPhase.WaitingCloseFromGapBuy => "WAITING CLOSE (GAP_SELL)",
            TradingFlowPhase.WaitingCloseFromGapSell => "WAITING CLOSE (GAP_BUY)",
            _ => "WAITING OPEN"
        };
    }

    private void BindDashboardMetrics(DashboardMetrics metrics)
    {
        ExchangeASymbol = FormatTextOrDash(metrics.ExchangeA.Symbol);
        ExchangeABid = FormatTrimmedNumberOrDash(metrics.ExchangeA.Bid);
        ExchangeAAsk = FormatTrimmedNumberOrDash(metrics.ExchangeA.Ask);
        ExchangeASpread = FormatTrimmedNumberOrDash(metrics.ExchangeA.Spread);
        ExchangeALatencyMs = FormatNumberOrDash(metrics.ExchangeA.LatencyMs, 0);
        ExchangeATps = FormatOneDecimalOrDash(metrics.ExchangeA.Tps);
        ExchangeATime = FormatTextOrDash(metrics.ExchangeA.Time);
        ExchangeAMaxLatMs = FormatNumberOrDash(metrics.ExchangeA.MaxLatMs, 0);
        ExchangeAAvgLatMs = FormatNumberOrDash(metrics.ExchangeA.AvgLatMs, 0);

        ExchangeBSymbol = FormatTextOrDash(metrics.ExchangeB.Symbol);
        ExchangeBBid = FormatTrimmedNumberOrDash(metrics.ExchangeB.Bid);
        ExchangeBAsk = FormatTrimmedNumberOrDash(metrics.ExchangeB.Ask);
        ExchangeBSpread = FormatTrimmedNumberOrDash(metrics.ExchangeB.Spread);
        ExchangeBLatencyMs = FormatNumberOrDash(metrics.ExchangeB.LatencyMs, 0);
        ExchangeBTps = FormatOneDecimalOrDash(metrics.ExchangeB.Tps);
        ExchangeBTime = FormatTextOrDash(metrics.ExchangeB.Time);
        ExchangeBMaxLatMs = FormatNumberOrDash(metrics.ExchangeB.MaxLatMs, 0);
        ExchangeBAvgLatMs = FormatNumberOrDash(metrics.ExchangeB.AvgLatMs, 0);

        GapBuy = FormatIntegerOrDash(metrics.GapBuy);
        GapSell = FormatIntegerOrDash(metrics.GapSell);
    }

    private static string FormatNumberOrDash(decimal? value, int decimalPlaces = 5)
        => value.HasValue
            ? value.Value.ToString($"F{decimalPlaces}", CultureInfo.InvariantCulture)
            : "-";

    private static string FormatTrimmedNumberOrDash(decimal? value, int maxDecimalPlaces = 5)
        => value.HasValue
            ? value.Value.ToString($"0.{new string('#', maxDecimalPlaces)}", CultureInfo.InvariantCulture)
            : "-";

    private static string FormatTrimmedNumberOrDash(float? value, int maxDecimalPlaces = 5)
        => value.HasValue
            ? value.Value.ToString($"0.{new string('#', maxDecimalPlaces)}", CultureInfo.InvariantCulture)
            : "-";

    private static string FormatOneDecimalOrDash(float? value)
        => value.HasValue
            ? value.Value.ToString("F1", CultureInfo.InvariantCulture)
            : "-";

    private static string FormatIntegerOrDash(int? value)
        => value.HasValue
            ? value.Value.ToString(CultureInfo.InvariantCulture)
            : "-";

    private static string FormatTextOrDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value;
}
