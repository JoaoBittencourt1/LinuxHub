using System.Globalization;
using System.Reflection;
using System.Windows;
using LinuxHub.Common.Appearance;
using LinuxHub.Common.Localization;
using LinuxHub.Common.Models;
using LinuxHub.Common.Ui;
using LinuxHub.Features.Catalog.ViewModels;
using LinuxHub.Features.Catalog.Views;
using LinuxHub.Features.InstallWizard.ViewModels;
using LinuxHub.Features.InstallWizard.Views;
using Wpf.Ui.Controls;

namespace LinuxHub.Shell
{
    /// <summary>
    /// Application shell: hosts feature views and language/theme chrome. No business logic.
    /// Navigation swaps ContentControl.Content via Button.Click (deterministic).
    /// </summary>
    public partial class MainWindow : FluentWindow
    {
        private readonly CatalogView _catalogView;
        private readonly InstallWizardView _installWizardView;
        private readonly InstallWizardViewModel _installWizardViewModel;
        private readonly IThemeService _themeService;

        public MainWindow(
            CatalogViewModel catalogViewModel,
            InstallWizardViewModel installWizardViewModel,
            IThemeService themeService)
        {
            ArgumentNullException.ThrowIfNull(catalogViewModel);
            ArgumentNullException.ThrowIfNull(installWizardViewModel);
            ArgumentNullException.ThrowIfNull(themeService);

            InitializeComponent();

            _themeService = themeService;
            _installWizardViewModel = installWizardViewModel;
            catalogViewModel.InstallRequested += OnInstallRequested;

            Version? version = Assembly.GetExecutingAssembly().GetName().Version;
            VersionText.Text = version is null ? string.Empty : $"v{version.Major}.{version.Minor}.{version.Build}";

            _catalogView = new CatalogView { DataContext = catalogViewModel };
            _installWizardView = new InstallWizardView { DataContext = installWizardViewModel };

            RefreshThemeIcon();
            ShowCatalog();
        }

        /// <summary>Distro detail "install now" shortcut: switch panel and preselect distro.</summary>
        private void OnInstallRequested(DistroInfo distro)
        {
            _installWizardViewModel.Iso.PrepareForDistro(distro);
            ShowInstallWizard();
        }

        private void CatalogNavButton_Click(object sender, RoutedEventArgs e) => ShowCatalog();

        private void InstallNavButton_Click(object sender, RoutedEventArgs e) => ShowInstallWizard();

        private void ShowCatalog()
        {
            ContentHost.Content = _catalogView;
            CatalogNavButton.Appearance = ControlAppearance.Secondary;
            InstallNavButton.Appearance = ControlAppearance.Transparent;
            ContentTransition.PlayEnter(_catalogView);
        }

        private void ShowInstallWizard()
        {
            ContentHost.Content = _installWizardView;
            InstallNavButton.Appearance = ControlAppearance.Secondary;
            CatalogNavButton.Appearance = ControlAppearance.Transparent;
            ContentTransition.PlayEnter(_installWizardView);
        }

        private void ThemeButton_Click(object sender, RoutedEventArgs e)
        {
            _themeService.Toggle();
            RefreshThemeIcon();
        }

        private void RefreshThemeIcon()
        {
            // Light mode → show moon (switch to dark). Dark mode → show sun (switch to light).
            ThemeIcon.Symbol = _themeService.IsDark ? SymbolRegular.WeatherSunny24 : SymbolRegular.WeatherMoon24;
            ThemeButton.ToolTip = LocalizationManager.Instance[
                _themeService.IsDark ? "Shell_ThemeSwitchToLight" : "Shell_ThemeSwitchToDark"];
        }

        private void LanguageButton_Click(object sender, RoutedEventArgs e)
        {
            LanguageMenu.PlacementTarget = LanguageButton;
            LanguageMenu.IsOpen = true;
        }

        private void PortugueseMenuItem_Click(object sender, RoutedEventArgs e) =>
            LocalizationManager.Instance.SetLanguage(new CultureInfo("pt-BR"));

        private void EnglishMenuItem_Click(object sender, RoutedEventArgs e) =>
            LocalizationManager.Instance.SetLanguage(new CultureInfo("en-US"));
    }
}
