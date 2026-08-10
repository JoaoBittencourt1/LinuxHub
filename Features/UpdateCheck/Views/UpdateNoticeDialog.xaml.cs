using LinuxHub.Features.UpdateCheck.ViewModels;
using Wpf.Ui.Controls;

namespace LinuxHub.Features.UpdateCheck.Views
{
    /// <summary>
    /// Primeiro diálogo estilizado do projeto. Os demais avisos usam <c>MessageBox.Show</c>
    /// nativo, que não acompanha o tema Fluent escuro aplicado em App.OnStartup — migrá-los
    /// é escopo de outro change; este estabelece o padrão.
    /// </summary>
    internal partial class UpdateNoticeDialog : FluentWindow
    {
        public UpdateNoticeDialog(UpdateNoticeViewModel viewModel)
        {
            ArgumentNullException.ThrowIfNull(viewModel);

            InitializeComponent();

            DataContext = viewModel;
            viewModel.CloseRequested += Close;
        }
    }
}
