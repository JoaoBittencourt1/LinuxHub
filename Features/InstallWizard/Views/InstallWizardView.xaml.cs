using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using LinuxHub.Common.Localization;
using LinuxHub.Features.InstallWizard.ViewModels;
using Microsoft.Win32;

namespace LinuxHub.Features.InstallWizard.Views
{
    /// <summary>
    /// ui:PasswordBox não suporta data binding de Password (por design, por
    /// segurança) — por isso o code-behind empurra PasswordChanged pra
    /// AccountViewModel.Password/ConfirmPassword, que são a fonte de verdade.
    /// </summary>
    public partial class InstallWizardView : UserControl
    {
        public InstallWizardView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is InstallWizardViewModel oldVm)
            {
                oldVm.Notify -= OnNotify;
                oldVm.InstallLog.CollectionChanged -= OnInstallLogChanged;
            }

            if (e.NewValue is InstallWizardViewModel newVm)
            {
                newVm.Notify += OnNotify;
                newVm.InstallLog.CollectionChanged += OnInstallLogChanged;
                newVm.RaiseStartupWarnings();
            }
        }

        /// <summary>ScrollViewer não acompanha o conteúdo sozinho quando ele cresce por
        /// binding — sem isto, cada passo novo do log nasceria fora da área visível e o
        /// usuário teria que rolar manualmente a cada linha.</summary>
        private void OnInstallLogChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
            InstallLogScroll.ScrollToEnd();

        private void OnNotify(string title, string message, bool isError) =>
            MessageBox.Show(message, title, MessageBoxButton.OK, isError ? MessageBoxImage.Error : MessageBoxImage.Information);

        private async void BrowseIso_Click(object sender, RoutedEventArgs e)
        {
            var viewModel = (InstallWizardViewModel)DataContext;

            var dialog = new OpenFileDialog
            {
                Title = LocalizationManager.Instance["Wizard_BrowseIsoDialogTitle"],
                Filter = LocalizationManager.Instance["Wizard_BrowseIsoDialogFilter"],
                CheckFileExists = true
            };

            if (dialog.ShowDialog() != true)
                return;

            // `async void`: exceção aqui não tem quem a observe e derruba o processo. E a
            // seleção manual agora ABRE o arquivo para calcular o hash, então disco removível
            // retirado, arquivo em uso ou permissão negada são falhas esperadas — não bug.
            try
            {
                await viewModel.Iso.SelectManualIsoAsync(dialog.FileName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                OnNotify(
                    LocalizationManager.Instance["Wizard_InstallErrorTitle"],
                    ex.Message,
                    isError: true);
            }
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e) =>
            ((InstallWizardViewModel)DataContext).Account.Password = PasswordBox.Password;

        private void ConfirmPasswordBox_PasswordChanged(object sender, RoutedEventArgs e) =>
            ((InstallWizardViewModel)DataContext).Account.ConfirmPassword = ConfirmPasswordBox.Password;
    }
}
