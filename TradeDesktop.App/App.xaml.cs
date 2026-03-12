using System.Windows;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TradeDesktop.App.ViewModels;
using TradeDesktop.App.State;
using TradeDesktop.Application.Abstractions;
using TradeDesktop.Application;
using TradeDesktop.Infrastructure;

namespace TradeDesktop.App;

public partial class App : System.Windows.Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                var configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(LoadDotEnv())
                    .AddEnvironmentVariables()
                    .Build();

                services
                    .AddApplication()
                    .AddInfrastructure(configuration);

                services.AddSingleton<RuntimeConfigState>();
                services.AddSingleton<IRuntimeConfigProvider>(sp => sp.GetRequiredService<RuntimeConfigState>());
                services.AddSingleton<IRuntimeConfigStateUpdater>(sp => sp.GetRequiredService<RuntimeConfigState>());
                services.AddSingleton<DashboardViewModel>();
                services.AddTransient<ConfigViewModel>();
                services.AddTransient<ConfigWindow>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        await _host.StartAsync();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        base.OnExit(e);
    }

    private static IDictionary<string, string?> LoadDotEnv()
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var envPath = FindDotEnvPath();

        if (envPath is null)
        {
            return values;
        }

        foreach (var rawLine in File.ReadLines(envPath))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
            {
                line = line[7..].TrimStart();
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var value = line[(separatorIndex + 1)..].Trim();
            if (value.Length >= 2 &&
                ((value.StartsWith('"') && value.EndsWith('"')) ||
                 (value.StartsWith('\'') && value.EndsWith('\''))))
            {
                value = value[1..^1];
            }

            values[key] = value;
        }

        return values;
    }

    private static string? FindDotEnvPath()
    {
        static string? FindInCurrentAndParents(string startDirectory)
        {
            var current = new DirectoryInfo(startDirectory);
            while (current is not null)
            {
                var candidate = Path.Combine(current.FullName, ".env");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                current = current.Parent;
            }

            return null;
        }

        var fromBaseDirectory = FindInCurrentAndParents(AppContext.BaseDirectory);
        if (fromBaseDirectory is not null)
        {
            return fromBaseDirectory;
        }

        return FindInCurrentAndParents(Directory.GetCurrentDirectory());
    }
}