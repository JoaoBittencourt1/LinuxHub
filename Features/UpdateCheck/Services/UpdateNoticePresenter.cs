using System.Reflection;
using System.Windows;
using LinuxHub.Common.Diagnostics;
using LinuxHub.Features.UpdateCheck.ViewModels;
using LinuxHub.Features.UpdateCheck.Views;

namespace LinuxHub.Features.UpdateCheck.Services
{
    /// <summary>
    /// Orquestra a checagem de atualização no startup: consulta, compara e — só se houver
    /// versão nova — exibe o aviso sobre a janela principal.
    ///
    /// Este é o ponto ÚNICO onde a política "falhou? registra e segue" vive. O service de
    /// checagem não captura exceção nenhuma (constitution §4 proíbe catch silencioso);
    /// concentrar o tratamento aqui é o que impede um catch genérico de se espalhar por ele.
    /// </summary>
    internal sealed class UpdateNoticePresenter
    {
        private readonly IUpdateCheckService _updateCheckService;

        public UpdateNoticePresenter(IUpdateCheckService updateCheckService)
        {
            ArgumentNullException.ThrowIfNull(updateCheckService);
            _updateCheckService = updateCheckService;
        }

        /// <summary>
        /// Dispara a checagem sem bloquear quem chamou — a janela principal já está visível
        /// e continua utilizável durante toda a espera de rede.
        ///
        /// A tarefa é explicitamente encadeada com um tratamento de falha em vez de virar um
        /// <c>async void</c> solto: exceção perdida numa tarefa esquecida nunca chegaria ao
        /// log, que é justamente a única evidência que este recurso tem.
        /// </summary>
        public void CheckInBackground(Window owner)
        {
            ArgumentNullException.ThrowIfNull(owner);

            _ = CheckAsync(owner);
        }

        private async Task CheckAsync(Window owner)
        {
            try
            {
                LatestRelease latest = await _updateCheckService.GetLatestReleaseAsync()
                    .ConfigureAwait(false);

                Version running = RunningVersion();

                if (!ReleaseVersionParser.IsOutdated(running, latest.Version))
                    return;

                // Voltar para a thread de UI: a chamada acima terminou numa thread de pool.
                await owner.Dispatcher.InvokeAsync(() => ShowNotice(owner, running, latest));
            }
            catch (Exception ex)
            {
                // Estar sem internet é situação esperada para este app, não defeito — um erro
                // exibido aqui não geraria relato útil e ensinaria o usuário a fechar os avisos
                // do app sem ler, o que é perigoso porque os outros avisos tratam de
                // reparticionamento de disco. Registra e segue.
                HttpErrorLog.Write(GitHubUpdateCheckService.LatestReleaseUrl, ex);
            }
        }

        private static void ShowNotice(Window owner, Version running, LatestRelease latest)
        {
            // A janela principal pode ter sido fechada enquanto a rede respondia (app aberto e
            // fechado rápido, ou resposta lenta). Atribuir Owner a uma janela já fechada lança;
            // nesse caso o aviso simplesmente não faz mais sentido.
            if (!owner.IsLoaded)
                return;

            var viewModel = new UpdateNoticeViewModel(running, latest.Version, latest.Url);
            var dialog = new UpdateNoticeDialog(viewModel) { Owner = owner };

            dialog.ShowDialog();
        }

        /// <summary>
        /// Mesma fonte que a versão exibida na TitleBar (Shell/MainWindow.xaml.cs): o assembly.
        /// Um segundo lugar declarando a versão divergiria e faria o aviso mentir sobre o que
        /// o usuário está rodando.
        /// </summary>
        private static Version RunningVersion() =>
            Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
    }
}
