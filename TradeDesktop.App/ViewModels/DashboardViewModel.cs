using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using TradeDesktop.App.Commands;
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
    private readonly IDashboardMetricsMapper _dashboardMetricsMapper;
    private readonly IMachineIdentityService _machineIdentityService;

    private string _runtimeSummary = string.Empty;
    private string _dbInlineData = string.Empty;
    private bool _isDbInlineDataVisible;
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
    private bool _isLoading = true;
    private string _loadingMessage = "Đang chờ dữ liệu shared memory...";

    public DashboardViewModel(
        IServiceProvider serviceProvider,
        RuntimeConfigState runtimeConfigState,
        IConfigService configService,
        IExchangePairReader exchangePairReader,
        IDashboardMetricsMapper dashboardMetricsMapper,
        IMachineIdentityService machineIdentityService)
    {
        _serviceProvider = serviceProvider;
        _runtimeConfigState = runtimeConfigState;
        _dashboardMetricsMapper = dashboardMetricsMapper;
        _machineIdentityService = machineIdentityService;

        var rawHostName = _machineIdentityService.GetRawHostName();
        var normalizedHostName = _machineIdentityService.GetHostName();

        LogItems = new ObservableCollection<string>
        {
            "[App] Dashboard started.",
            $"[Config] Detected host name: {rawHostName}",
            $"[Config] Normalized host name: {normalizedHostName}",
            "[App] Waiting for runtime config and shared memory data..."
        };

        OpenConfigCommand = new AsyncRelayCommand(OpenConfigAsync);
        ClearLogsCommand = new AsyncRelayCommand(ClearLogsAsync);

        _runtimeConfigState.StateChanged += (_, _) => ApplyRuntimeConfig();
        ApplyRuntimeConfig();
        _ = InitializeRuntimeConfigAsync(configService);

        exchangePairReader.SnapshotReceived += OnSnapshotReceived;
        _ = exchangePairReader.StartAsync();
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

    public AsyncRelayCommand OpenConfigCommand { get; }
    public AsyncRelayCommand ClearLogsCommand { get; }

    private Task OpenConfigAsync()
    {
        try
        {
            var configWindow = _serviceProvider.GetRequiredService<ConfigWindow>();
            configWindow.Owner = System.Windows.Application.Current.MainWindow;
            configWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            LogItems.Insert(0, $"[Config] Không mở được cửa sổ Config: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private Task ClearLogsAsync()
    {
        LogItems.Clear();
        return Task.CompletedTask;
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
            $"Host Name: {_runtimeConfigState.CurrentMachineHostName}  |  Point: {_runtimeConfigState.CurrentPoint}  |  Map 1: {_runtimeConfigState.CurrentMapName1}  |  Map 2: {_runtimeConfigState.CurrentMapName2}";
    }

    private async Task InitializeRuntimeConfigAsync(IConfigService configService)
    {
        const string InlineDbHostName = "win-vps-01";
        try
        {
            LoadingMessage = "Đang tải cấu hình runtime...";

            var result = await configService.LoadByMachineHostNameAsync();
            if (result.IsSuccess && result.Exists)
            {
                _runtimeConfigState.Update(result.MachineHostName, result.MapName1, result.MapName2, result.Point);

                if (string.Equals(result.MachineHostName, InlineDbHostName, StringComparison.OrdinalIgnoreCase))
                {
                    DbInlineData =
                        $"[DB] id={result.ConfigId} | hostname={result.MachineHostName} | point={result.Point} | sans={result.SansJson}";
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

    private void OnSnapshotReceived(object? sender, SharedMemorySnapshot snapshot)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var metrics = _dashboardMetricsMapper.Map(snapshot);
            _runtimeConfigState.UpdateDashboardMetrics(metrics);
            IsLoading = false;

            BindDashboardMetrics(metrics);

            LogItems.Insert(0,
                $"[{DateTime.Now:HH:mm:ss}] A:{metrics.ExchangeA.Symbol} ({(metrics.IsConnectedA ? "ON" : "OFF")}) | B:{metrics.ExchangeB.Symbol} ({(metrics.IsConnectedB ? "ON" : "OFF")})");

            while (LogItems.Count > 200)
            {
                LogItems.RemoveAt(LogItems.Count - 1);
            }
        });
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
