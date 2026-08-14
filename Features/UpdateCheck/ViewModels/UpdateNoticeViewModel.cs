using System.Diagnostics;
using System.Windows.Input;
using LinuxHub.Common.Diagnostics;
using LinuxHub.Common.Localization;
using LinuxHub.Common.Mvvm;

namespace LinuxHub.Features.UpdateCheck.ViewModels
{
    /// <summary>
    /// Estado do aviso de nova versão. Não bloqueia nada: as duas saídas (baixar ou
    /// dispensar) devolvem o usuário ao app funcionando.
    /// </summary>
    internal sealed class UpdateNoticeViewModel : ObservableObject
    {
        private readonly Uri _releaseUrl;

        public UpdateNoticeViewModel(Version runningVersion, Version latestVersion, Uri releaseUrl)
        {
            ArgumentNullException.ThrowIfNull(runningVersion);
            ArgumentNullException.ThrowIfNull(latestVersion);
            ArgumentNullException.ThrowIfNull(releaseUrl);

            _releaseUrl = releaseUrl;
            RunningVersion = runningVersion;
            LatestVersion = latestVersion;

            DownloadCommand = new RelayCommand(Download);
            DismissCommand = new RelayCommand(() => CloseRequested?.Invoke());
        }

        public Version RunningVersion { get; }

        public Version LatestVersion { get; }

        /// <summary>
        /// Composta em código (e não com <c>loc:Loc</c> direto no XAML) porque interpola as
        /// duas versões. Não assina a troca de idioma do <see cref="LocalizationManager"/>:
        /// o diálogo é modal sobre a janela principal, então o botão de idioma fica
        /// inalcançável enquanto ele está aberto — assinar só criaria um vazamento a limpar.
        /// </summary>
        public string VersionsText => LocalizationManager.Instance.Format(
            "UpdateNotice_Versions",
            $"{RunningVersion.Major}.{RunningVersion.Minor}.{RunningVersion.Build}",
            $"{LatestVersion.Major}.{LatestVersion.Minor}.{LatestVersion.Build}");

        public ICommand DownloadCommand { get; }

        public ICommand DismissCommand { get; }

        /// <summary>Pedido de fechar o diálogo — a View decide como.</summary>
        public event Action? CloseRequested;

        private void Download()
        {
            // A URL veio de uma resposta de rede, e UseShellExecute honra outros esquemas
            // além de http (file:, e o que mais estiver registrado no Windows). Este app já
            // roda operações elevadas — entregar valor de origem externa ao shell sem
            // conferir o esquema não é risco que se aceite aqui. Ver decisão 9 do design.md.
            if (!_releaseUrl.IsAbsoluteUri ||
                (_releaseUrl.Scheme != Uri.UriSchemeHttp && _releaseUrl.Scheme != Uri.UriSchemeHttps))
            {
                HttpErrorLog.Write(
                    _releaseUrl.ToString(),
                    "URL de release recusada: esquema não é http/https. Nada foi aberto.");
                CloseRequested?.Invoke();
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _releaseUrl.ToString(),
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                // Falhar ao abrir o navegador não vira erro na cara do usuário: este aviso
                // inteiro é opcional e não pode atrapalhar quem só quer usar o app. Fica no
                // log, como as demais falhas deste recurso.
                HttpErrorLog.Write(_releaseUrl.ToString(), ex);
            }

            CloseRequested?.Invoke();
        }
    }
}
