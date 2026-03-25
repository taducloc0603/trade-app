using System.Globalization;
using System.Collections.ObjectModel;
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

    private static readonly TimeSpan OrderInfoPollInterval = TimeSpan.FromSeconds(1);

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
            new OrderPanelStatusViewModel("Sàn A"),
            new OrderPanelStatusViewModel("Sàn B"));

        HistoryTab = new OrderInfoTabViewModel(
            OrderTabType.History,
            "History",
            new OrderPanelStatusViewModel("Sàn A"),
            new OrderPanelStatusViewModel("Sàn B"));

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
        var result = await _mt5ManualTradeService.ExecuteBuyAsync(
            _runtimeConfigState.CurrentChartHwndA,
            _runtimeConfigState.CurrentChartHwndB);

        AppendManualTradeLogs(result, snapshot);
        ShowManualTradeFeedback("BUY", result);
    }

    private async Task SellAsync()
    {
        var snapshot = _runtimeConfigState.CurrentDashboardMetrics;
        var result = await _mt5ManualTradeService.ExecuteSellAsync(
            _runtimeConfigState.CurrentChartHwndA,
            _runtimeConfigState.CurrentChartHwndB);

        AppendManualTradeLogs(result, snapshot);
        ShowManualTradeFeedback("SELL", result);
    }

    private async Task CloseOrderAsync()
    {
        var snapshot = _runtimeConfigState.CurrentDashboardMetrics;
        var result = await _mt5ManualTradeService.ExecuteCloseAsync(
            _runtimeConfigState.CurrentTradeHwndA,
            _runtimeConfigState.CurrentTradeHwndB);

        AppendManualTradeLogs(result, snapshot);
        ShowManualTradeFeedback("CLOSE", result);
    }

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

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                ApplyTradeResult(TradeTab.LeftPanel, tradeLeftResult);
                ApplyTradeResult(TradeTab.RightPanel, tradeRightResult);
                ApplyHistoryResult(HistoryTab.LeftPanel, historyLeftResult);
                ApplyHistoryResult(HistoryTab.RightPanel, historyRightResult);
            });
        }
    }

    private static void ApplyTradeResult(
        OrderPanelStatusViewModel panel,
        SharedMapReadResult<TradeSharedRecord> result)
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

        if (result.Records.Count == 0)
        {
            panel.SetEmpty();
            return;
        }

        var first = result.Records[0];
        var summaries = result.Records
            .Select(r => new OrderRecordItemViewModel(
                $"#{r.Ticket} | {(r.TradeType == 0 ? "BUY" : "SELL")} | Lot {r.Lot:0.##} | Profit {r.Profit:0.##}"))
            .ToArray();

        var leftFields = new[]
        {
            new OrderInfoFieldViewModel("Ticket", first.Ticket.ToString(CultureInfo.InvariantCulture)),
            new OrderInfoFieldViewModel("Type", first.TradeType == 0 ? "BUY" : "SELL"),
            new OrderInfoFieldViewModel("Lot", first.Lot.ToString("0.#####", CultureInfo.InvariantCulture)),
            new OrderInfoFieldViewModel("Open Time", first.OpenTime.ToString(CultureInfo.InvariantCulture))
        };

        var rightFields = new[]
        {
            new OrderInfoFieldViewModel("Price", first.Price.ToString("0.#####", CultureInfo.InvariantCulture)),
            new OrderInfoFieldViewModel("SL", first.Sl.ToString("0.#####", CultureInfo.InvariantCulture)),
            new OrderInfoFieldViewModel("TP", first.Tp.ToString("0.#####", CultureInfo.InvariantCulture)),
            new OrderInfoFieldViewModel("Profit", first.Profit.ToString("0.#####", CultureInfo.InvariantCulture))
        };

        panel.SetData(summaries, leftFields, rightFields);
    }

    private static void ApplyHistoryResult(
        OrderPanelStatusViewModel panel,
        SharedMapReadResult<HistorySharedRecord> result)
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

        if (result.Records.Count == 0)
        {
            panel.SetEmpty();
            return;
        }

        var first = result.Records[0];
        var summaries = result.Records
            .Select(r => new OrderRecordItemViewModel(
                $"#{r.Ticket} | Vol {r.Volume:0.##} | Profit {r.Profit:0.##}"))
            .ToArray();

        var leftFields = new[]
        {
            new OrderInfoFieldViewModel("Ticket", first.Ticket.ToString(CultureInfo.InvariantCulture)),
            new OrderInfoFieldViewModel("Volume", first.Volume.ToString("0.#####", CultureInfo.InvariantCulture))
        };

        var rightFields = new[]
        {
            new OrderInfoFieldViewModel("Profit", first.Profit.ToString("0.#####", CultureInfo.InvariantCulture)),
            new OrderInfoFieldViewModel("Deal Time", first.DealTime.ToString(CultureInfo.InvariantCulture))
        };

        panel.SetData(summaries, leftFields, rightFields);
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
        });
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
