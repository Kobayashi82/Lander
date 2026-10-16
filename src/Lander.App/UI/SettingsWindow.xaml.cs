using System.Windows;
using System.Windows.Controls;
using Lander.App.Engine;
using Lander.App.Services;

namespace Lander.App.UI;

/// <summary>Ajustes con el aspecto de la Configuración de Windows 11 (tema Fluent de WPF).</summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private readonly FrameworkElement[] _pages;

    internal SettingsWindow(SettingsService settings, App app)
    {
        InitializeComponent();
        _viewModel = new SettingsViewModel(settings, app);
        DataContext = _viewModel;
        Icon = IconFactory.RenderLander(64);
        _pages = [GeneralPage, AppearancePage, BehaviorPage, PilotingPage, SoundPage, PilotPage, AboutPage];
    }

    internal void RefreshStatus(EngineStatus status) => _viewModel.RefreshStatus(status);

    private void OnNavigationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_pages is null)
        {
            return;
        }

        for (var i = 0; i < _pages.Length; i++)
        {
            _pages[i].Visibility = i == Navigation.SelectedIndex ? Visibility.Visible : Visibility.Collapsed;
        }

        Scroller.ScrollToTop();
    }
}
