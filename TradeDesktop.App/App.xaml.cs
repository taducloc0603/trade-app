using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TradeDesktop.App.ViewModels;
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
                    .AddEnvironmentVariables()
                    .Build();

                services
                    .AddApplication()
                    .AddInfrastructure(configuration);

                services.AddSingleton<DashboardViewModel>();
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
}