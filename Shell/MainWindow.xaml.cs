using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LinuxHub.Common.Animations;
using LinuxHub.Common.Localization;
using LinuxHub.Common.Models;
using LinuxHub.Common.Theming;
using LinuxHub.Features.Catalog.ViewModels;
using LinuxHub.Features.Catalog.Views;
using LinuxHub.Features.InstallWizard.ViewModels;
using LinuxHub.Features.InstallWizard.Views;
using Wpf.Ui.Controls;

namespace LinuxHub.Shell
{
    /// <summary>
    /// Shell da aplicação: hospeda as views de feature e troca o idioma. Sem lógica
    /// de negócio. Navegação é troca direta de ContentControl.Content via Click de
    /// botão — não usa o gerenciamento de conteúdo interno do NavigationView (que se
    /// mostrou pouco confiável em teste manual: SelectionChanged nem sempre refletia
    /// o item clicado). Button.Click é um evento simples e determinístico.
    /// </summary>
    public partial class MainWindow : FluentWindow
    {
        /// <summary>Deslocamento vertical do marcador até o segundo botão de navegação:
        /// altura do primeiro (42) mais a margem que os separa (6).</summary>
        private const double InstallNavIndicatorOffset = 48d;

        private static readonly Duration NavIndicatorDuration = new(TimeSpan.FromMilliseconds(340));

        private readonly CatalogView _catalogView;
        private readonly InstallWizardView _installWizardView;
        private readonly InstallWizardViewModel _installWizardViewModel;

        public MainWindow(CatalogViewModel catalogViewModel, InstallWizardViewModel installWizardViewModel)
        {
            ArgumentNullException.ThrowIfNull(catalogViewModel);
            ArgumentNullException.ThrowIfNull(installWizardViewModel);

            InitializeComponent();

            _installWizardViewModel = installWizardViewModel;
            catalogViewModel.InstallRequested += OnInstallRequested;

            Version? version = Assembly.GetExecutingAssembly().GetName().Version;
            VersionText.Text = version is null ? string.Empty : $"v{version.Major}.{version.Minor}.{version.Build}";

            _catalogView = new CatalogView { DataContext = catalogViewModel };
            _installWizardView = new InstallWizardView { DataContext = installWizardViewModel };

            ShowCatalog();
        }

        /// <summary>Atalho "instalar agora" da página da distro: além de trocar de painel,
        /// deixa o wizard já apontado pra ela — chegar na tela de instalação e ter que
        /// escolher a distro de novo era refazer a escolha que o usuário acabou de fazer.</summary>
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
            MoveNavIndicator(0d);
        }

        private void ShowInstallWizard()
        {
            ContentHost.Content = _installWizardView;
            InstallNavButton.Appearance = ControlAppearance.Secondary;
            CatalogNavButton.Appearance = ControlAppearance.Transparent;
            MoveNavIndicator(InstallNavIndicatorOffset);
        }

        private void MoveNavIndicator(double offsetY) =>
            NavIndicatorOffset.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(offsetY, NavIndicatorDuration)
                {
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 },
                });

        private void LanguageButton_Click(object sender, RoutedEventArgs e)
        {
            LanguageMenu.PlacementTarget = LanguageButton;
            LanguageMenu.IsOpen = true;
        }

        private void ThemeButton_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.Instance.Toggle();

            ThemeIcon.Symbol = ThemeManager.Instance.IsDark
                ? SymbolRegular.WeatherMoon24
                : SymbolRegular.WeatherSunny24;

            ThemeIconRotation.BeginAnimation(
                RotateTransform.AngleProperty,
                new DoubleAnimation(0d, 360d, new Duration(TimeSpan.FromMilliseconds(650)))
                {
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 },
                });

            Entrance.Play(RootGrid, EntranceEffect.Fade, TimeSpan.Zero);
        }

        private void PortugueseMenuItem_Click(object sender, RoutedEventArgs e) =>
            LocalizationManager.Instance.SetLanguage(new CultureInfo("pt-BR"));

        private void EnglishMenuItem_Click(object sender, RoutedEventArgs e) =>
            LocalizationManager.Instance.SetLanguage(new CultureInfo("en-US"));
    }
}
