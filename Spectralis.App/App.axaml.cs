using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Spectralis.App.Services;
using Spectralis.App.ViewModels;
using Spectralis.App.Views;
using Spectralis.Core.Infrastructure;
using Spectralis.Core.Library;

namespace Spectralis.App
{
    public partial class App : Application
    {
        private ServiceContainer? _services;
        private PositionService? _position;
        private TrayService? _tray;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            AppPaths.EnsureDirectoriesExist();

            _services = new ServiceContainer();
            var vm = new MainViewModel(_services);

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var window = new MainWindow { DataContext = vm };

                var hotkeyService = new HotkeyService();
                window.SetupHotkeys(hotkeyService, vm.Player);

                _position = new PositionService(_services.AudioEngine, vm.Player);
                _tray = new TrayService(_services.AudioEngine);
                _tray.ShowRequested += (s, e) => window.Show();

                _services.AudioEngine.TrackLoaded += (s, t) =>
                    _tray.UpdateTooltip($"{t.Artist} — {t.Title}");

                desktop.MainWindow = window;
                desktop.Exit += (s, e) =>
                {
                    _position?.Dispose();
                    _tray?.Dispose();
                    hotkeyService.Dispose();
                    _services?.Dispose();
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
