using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Media;
using TradeDesktop.App.Commands;
using TradeDesktop.App.State;
using TradeDesktop.Application.Abstractions;
using TradeDesktop.Application.Models;
using TradeDesktop.Application.Services;
using TradeDesktop.Domain.Models;

namespace TradeDesktop.App.ViewModels;

public sealed class DashboardViewModel : ObservableObject
{
    private const bool EnableGapDebugLog = true;
    private const bool IncludeHoldProgressDebugLog = false;
    private const int MaxLogItems = 300;

    private readonly IServiceProvider _serviceProvider;
    private readonly RuntimeConfigState _runtimeConfigState;
    private readonly IConfigService _configService;
    private readonly IDashboardMetricsMapper _dashboardMetricsMapper;
    private readonly IGapSignalConfirmationEngine _gapSignalConfirmationEngine;
    private readonly IMachineIdentityService _machineIdentityService;
    private readonly string _normalizedHostName;

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

    public DashboardViewModel(
        IServiceProvider serviceProvider,
        RuntimeConfigState runtimeConfigState,
        IConfigService configService,
        IExchangePairReader exchangePairReader,
        IDashboardMetricsMapper dashboardMetricsMapper,
        IGapSignalConfirmationEngine gapSignalConfirmationEngine,
        IMachineIdentityService machineIdentityService)
    {
        _serviceProvider = serviceProvider;
        _runtimeConfigState = runtimeConfigState;
        _configService = configService;
        _dashboardMetricsMapper = dashboardMetricsMapper;
        _gapSignalConfirmationEngine = gapSignalConfirmationEngine;
        _machineIdentityService = machineIdentityService;

        var rawHostName = _machineIdentityService.GetRawHostName();
        var normalizedHostName = _machineIdentityService.GetHostName();
        _normalizedHostName = normalizedHostName;
        MachineHostName = normalizedHostName;

        LogItems = new ObservableCollection<string>
        {
            "[App] Dashboard started.",
            $"[Config] Detected host name: {rawHostName}",
            $"[Config] Normalized host name: {normalizedHostName}",
            "[App] Waiting for runtime config and shared memory data...",
            $"{DateTime.Now:yyyy-M-d HH:mm:ss} | System | Trading logic stopped"
        };

        OpenConfigCommand = new AsyncRelayCommand(OpenConfigAsync);
        ReconnectConfigCommand = new AsyncRelayCommand(ReconnectConfigAsync);
        ClearLogsCommand = new AsyncRelayCommand(ClearLogsAsync);
        CopyHostNameCommand = new AsyncRelayCommand(CopyHostNameAsync);
        StartTradingLogicCommand = new AsyncRelayCommand(StartTradingLogicAsync, CanStartTradingLogic);
        StopTradingLogicCommand = new AsyncRelayCommand(StopTradingLogicAsync, CanStopTradingLogic);

        _runtimeConfigState.StateChanged += (_, _) => ApplyRuntimeConfig();
        ApplyRuntimeConfig();
        _ = InitializeRuntimeConfigAsync();

        exchangePairReader.SnapshotReceived += OnSnapshotReceived;
        _ = StartExchangeReaderSafeAsync(exchangePairReader);
    }

    public ObservableCollection<string> LogItems { get; }

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
    public string LastSignalText
    {
        get => _lastSignalText;
        private set => SetProperty(ref _lastSignalText, value);
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

    public AsyncRelayCommand OpenConfigCommand { get; }
    public AsyncRelayCommand ReconnectConfigCommand { get; }
    public AsyncRelayCommand ClearLogsCommand { get; }
    public AsyncRelayCommand CopyHostNameCommand { get; }
    public AsyncRelayCommand StartTradingLogicCommand { get; }
    public AsyncRelayCommand StopTradingLogicCommand { get; }

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

    private void ResetTradingLogicState()
    {
        _gapSignalConfirmationEngine.Reset();
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
                LogItems.Insert(0, "[Config] Host name rỗng, không thể copy.");
                return Task.CompletedTask;
            }

            System.Windows.Clipboard.SetText(hostNameToCopy);
            LogItems.Insert(0, $"[Config] Đã copy host name: {hostNameToCopy}");
        }
        catch (Exception ex)
        {
            LogItems.Insert(0, $"[Config] Không thể copy host name: {ex.Message}");
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
            LogItems.Insert(0, error);
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

    private Task ClearLogsAsync()
    {
        LogItems.Clear();
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
        LogItems.Insert(0, "[Config] Reconnect: đang tải lại config từ DB theo host name...");
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
            $"Host Name: {_runtimeConfigState.CurrentMachineHostName}  |  Point: {_runtimeConfigState.CurrentPoint}  |  OpenPts: {_runtimeConfigState.CurrentOpenPts}  |  ConfirmGapPts: {_runtimeConfigState.CurrentConfirmGapPts}  |  HoldConfirmMs: {_runtimeConfigState.CurrentHoldConfirmMs}  |  Map 1: {_runtimeConfigState.CurrentMapName1}  |  Map 2: {_runtimeConfigState.CurrentMapName2}";
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
                _runtimeConfigState.Update(
                    result.MachineHostName,
                    result.MapName1,
                    result.MapName2,
                    result.Point,
                    result.OpenPts,
                    result.ConfirmGapPts,
                    result.HoldConfirmMs);
                ResetTradingLogicState();

                if (string.Equals(result.MachineHostName, InlineDbHostName, StringComparison.OrdinalIgnoreCase))
                {
                    DbInlineData =
                        $"[DB] id={result.ConfigId} | hostname={result.MachineHostName} | point={result.Point} | open_pts={result.OpenPts} | confirm_gap_pts={result.ConfirmGapPts} | hold_confirm_ms={result.HoldConfirmMs} | sans={result.SansJson}";
                    IsDbInlineDataVisible = true;
                }
                else
                {
                    DbInlineData = string.Empty;
                    IsDbInlineDataVisible = false;
                }

                LoadingMessage = "Đang chờ dữ liệu shared memory...";
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    LogItems.Insert(0,
                        $"[Config] Reconnect thành công cho host '{result.MachineHostName}' (Map1: {result.MapName1}, Map2: {result.MapName2}).");
                });
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
                LogItems.Insert(0, warning);
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
                LogItems.Insert(0, $"[Config] Lỗi tải config runtime: {ex.Message}");
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
                LogItems.Insert(0, $"[Reader] Không thể start shared memory reader: {ex.Message}");
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

            if (IsTradingLogicEnabled)
            {
                var triggerResults = _gapSignalConfirmationEngine.ProcessSnapshot(
                    new GapSignalSnapshot(metrics.TimestampUtc, metrics.GapBuy, metrics.GapSell),
                    new GapSignalConfirmationConfig(
                        ConfirmGapPts: _runtimeConfigState.CurrentConfirmGapPts,
                        OpenPts: _runtimeConfigState.CurrentOpenPts,
                        HoldConfirmMs: _runtimeConfigState.CurrentHoldConfirmMs));

                if (EnableGapDebugLog)
                {
                    foreach (var debugEvent in _gapSignalConfirmationEngine.LastDebugEvents)
                    {
                        if (!IncludeHoldProgressDebugLog && debugEvent.Stage == GapSignalDebugStage.HoldProgress)
                        {
                            continue;
                        }

                        AppendLogItem(FormatGapDebugLog(debugEvent));
                    }
                }

                foreach (var trigger in triggerResults)
                {
                    if (!trigger.Triggered || trigger.Action != GapSignalAction.Open)
                    {
                        continue;
                    }

                    var sideText = trigger.Side == GapSignalSide.Buy ? "BUY" : "SELL";
                    var joinedGaps = string.Join("|", trigger.Gaps);
                    var triggeredAtLocal = trigger.TriggeredAtUtc.ToLocalTime();
                    LastSignalText = $"[{triggeredAtLocal:HH:mm:ss}] OPEN {sideText} ({joinedGaps})";
                    AppendLogItem($"{triggeredAtLocal:yyyy-M-d HH:mm:ss} | Open | {sideText} | {joinedGaps}");
                }
            }
        });
    }

    private void AppendLogItem(string message)
    {
        LogItems.Insert(0, message);

        while (LogItems.Count > MaxLogItems)
        {
            LogItems.RemoveAt(LogItems.Count - 1);
        }
    }

    private static string FormatGapDebugLog(GapSignalDebugEvent debugEvent)
    {
        var localTime = debugEvent.TimestampUtc.ToLocalTime();
        var sideText = debugEvent.Side == GapSignalSide.Buy ? "BUY" : "SELL";
        var gapText = debugEvent.Gap?.ToString(CultureInfo.InvariantCulture) ?? "null";

        return $"{localTime:yyyy-M-d HH:mm:ss} | Debug | {sideText} | stage={debugEvent.Stage} | gap={gapText} | elapsedMs={debugEvent.ElapsedMs:F0} | confirm={debugEvent.ConfirmGapPts} | open={debugEvent.OpenPts} | holdMs={debugEvent.HoldConfirmMs} | reason={debugEvent.Reason}";
    }

    private void BindDashboardMetrics(DashboardMetrics metrics)
    {
        ExchangeASymbol = FormatTextOrDash(metrics.ExchangeA.Symbol);
        ExchangeABid = FormatTrimmedNumberOrDash(metrics.ExchangeA.Bid);
        ExchangeAAsk = FormatTrimmedNumberOrDash(metrics.ExchangeA.Ask);
        ExchangeASpread = FormatTrimmedNumberOrDash(metrics.ExchangeA.Spread);
        ExchangeALatencyMs = FormatNumberOrDash(metrics.ExchangeA.LatencyMs, 0);
        ExchangeATps = FormatTrimmedNumberOrDash(metrics.ExchangeA.Tps, 5);
        ExchangeATime = FormatTextOrDash(metrics.ExchangeA.Time);
        ExchangeAMaxLatMs = FormatNumberOrDash(metrics.ExchangeA.MaxLatMs, 0);
        ExchangeAAvgLatMs = FormatNumberOrDash(metrics.ExchangeA.AvgLatMs, 0);

        ExchangeBSymbol = FormatTextOrDash(metrics.ExchangeB.Symbol);
        ExchangeBBid = FormatTrimmedNumberOrDash(metrics.ExchangeB.Bid);
        ExchangeBAsk = FormatTrimmedNumberOrDash(metrics.ExchangeB.Ask);
        ExchangeBSpread = FormatTrimmedNumberOrDash(metrics.ExchangeB.Spread);
        ExchangeBLatencyMs = FormatNumberOrDash(metrics.ExchangeB.LatencyMs, 0);
        ExchangeBTps = FormatTrimmedNumberOrDash(metrics.ExchangeB.Tps, 5);
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

    private static string FormatIntegerOrDash(int? value)
        => value.HasValue
            ? value.Value.ToString(CultureInfo.InvariantCulture)
            : "-";

    private static string FormatTextOrDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value;
}
