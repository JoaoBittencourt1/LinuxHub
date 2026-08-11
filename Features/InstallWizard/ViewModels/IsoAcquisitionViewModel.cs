using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using LinuxHub.Common.Data;
using LinuxHub.Common.Localization;
using LinuxHub.Common.Models;
using LinuxHub.Common.Mvvm;
using LinuxHub.Features.InstallWizard.Services;

namespace LinuxHub.Features.InstallWizard.ViewModels
{
    /// <summary>
    /// Fonte da ISO: download automático (com progresso/cancelamento) ou seleção manual
    /// com validação e detecção de distro. Ver specs/install-wizard/spec.md.
    /// </summary>
    public class IsoAcquisitionViewModel : ObservableObject
    {
        private const long MinimumIsoSizeBytes = 700L * 1024 * 1024;

        private readonly IIsoDownloadService _downloadService;
        private readonly IDistroDetectionService _detectionService;
        private readonly IDownloadedIsoRepository _downloadedIsoRepository;
        private readonly IArtifactVerifier _artifactVerifier;
        private readonly AsyncRelayCommand _downloadIsoCommand;
        private readonly RelayCommand _cancelDownloadCommand;
        private readonly RelayCommand _downloadAnotherIsoCommand;
        private readonly RelayCommand _useDownloadedIsoCommand;
        private CancellationTokenSource? _downloadCts;

        private bool _isManualSelect;
        private DistroInfo? _selectedDistro;
        private string? _manualIsoPath;
        private DistroInfo? _detectedDistro;
        private bool _isManualIsoVersionUncertain;
        private DownloadedIso? _selectedDownloadedIso;
        private bool _isChoosingNewDownload;
        private bool _useAutoinstall = true;
        private bool _isDownloading;
        private double _downloadPercent;
        private bool _isDownloadIndeterminate;
        private string _downloadStatusText = string.Empty;
        private bool _isVerifyingManualIso;
        private double _manualVerificationPercent;

        public IsoAcquisitionViewModel(
            IIsoDownloadService downloadService,
            IDistroDetectionService detectionService,
            IDownloadedIsoRepository downloadedIsoRepository,
            IArtifactVerifier artifactVerifier)
        {
            _downloadService = downloadService ?? throw new ArgumentNullException(nameof(downloadService));
            _detectionService = detectionService ?? throw new ArgumentNullException(nameof(detectionService));
            _downloadedIsoRepository = downloadedIsoRepository ?? throw new ArgumentNullException(nameof(downloadedIsoRepository));
            _artifactVerifier = artifactVerifier ?? throw new ArgumentNullException(nameof(artifactVerifier));

            // Comandos antes de qualquer atribuição a propriedade: o setter de SelectedDistro
            // chama _downloadIsoCommand.RaiseCanExecuteChanged(), então o campo precisa existir
            // antes da primeira atribuição a SelectedDistro logo abaixo.
            _downloadIsoCommand = new AsyncRelayCommand(
                DownloadAsync,
                () => !IsManualSelect && SelectedDistro is { HasVerifiableArtifact: true } && !IsDownloading);
            _cancelDownloadCommand = new RelayCommand(() => _downloadCts?.Cancel(), () => IsDownloading);
            _downloadAnotherIsoCommand = new RelayCommand(() => IsChoosingNewDownload = true);
            _useDownloadedIsoCommand = new RelayCommand(UseDownloadedIso);

            // Uma entrada desabilitada (DistroInfo.IsEnabled) não é oferecida no seletor de
            // download — permanece só nos dados, ver comentário de cada entrada em
            // DistroCatalog. Seleção manual continua reconhecendo o arquivo pelo nome mesmo
            // assim (DistroDetectionService não filtra por IsEnabled).
            Distros = DistroCatalog.All.Where(distro => distro.IsEnabled).ToList();
            SelectedDistro = Distros.Count > 0 ? Distros[0] : null;

            DownloadedIsos = new ObservableCollection<DownloadedIso>(_downloadedIsoRepository.GetAll());
            // A mais recente vem primeiro (ordenação do repositório) — é o ponto de
            // partida mais provável do usuário que acabou de baixar algo. Só quando não
            // há nenhuma é que a tela abre direto no modo de escolher uma distro nova.
            if (DownloadedIsos.Count > 0)
                SelectedDownloadedIso = DownloadedIsos[0];
            else
                _isChoosingNewDownload = true;
        }

        public IReadOnlyList<DistroInfo> Distros { get; }

        public ObservableCollection<DownloadedIso> DownloadedIsos { get; }

        public bool HasDownloadedIsos => DownloadedIsos.Count > 0;

        /// <summary>Lista de ISOs já baixadas: só faz sentido mostrar as duas coisas juntas
        /// (a lista e o combo de "baixar outra") se o usuário pediu explicitamente pra trocar —
        /// caso contrário são dois campos disputando a mesma decisão, sem indicar qual vale.</summary>
        public bool IsDownloadedIsosVisible => !IsManualSelect && HasDownloadedIsos && !IsChoosingNewDownload;

        /// <summary>Combo de "escolher distro + baixar": aparece quando não há nada baixado
        /// ainda, ou quando o usuário clicou em "baixar outra ISO".</summary>
        public bool IsDistroPickerVisible => !IsManualSelect && (!HasDownloadedIsos || IsChoosingNewDownload);

        /// <summary>Só faz sentido oferecer "voltar" se existe pra onde voltar.</summary>
        public bool IsBackToDownloadedVisible => !IsManualSelect && HasDownloadedIsos && IsChoosingNewDownload && !IsDownloading;

        private bool IsChoosingNewDownload
        {
            get => _isChoosingNewDownload;
            set
            {
                if (!SetProperty(ref _isChoosingNewDownload, value))
                    return;

                OnPropertyChanged(nameof(IsDownloadedIsosVisible));
                OnPropertyChanged(nameof(IsDistroPickerVisible));
                OnPropertyChanged(nameof(IsBackToDownloadedVisible));
            }
        }

        /// <summary>ISO já baixada escolhida pelo usuário para instalar sem baixar de novo.</summary>
        public DownloadedIso? SelectedDownloadedIso
        {
            get => _selectedDownloadedIso;
            set
            {
                if (!SetProperty(ref _selectedDownloadedIso, value))
                    return;

                ResolvedIsoPath = value?.Path;
                NotifyIsoSelectionChanged();
            }
        }

        public bool IsManualSelect
        {
            get => _isManualSelect;
            set
            {
                if (SetProperty(ref _isManualSelect, value))
                {
                    OnPropertyChanged(nameof(IsManualIsoVisible));
                    OnPropertyChanged(nameof(IsDistroDisplayVisible));
                    OnPropertyChanged(nameof(IsDownloadButtonVisible));
                    OnPropertyChanged(nameof(IsDownloadedIsosVisible));
                    OnPropertyChanged(nameof(IsDistroPickerVisible));
                    OnPropertyChanged(nameof(IsBackToDownloadedVisible));
                    NotifyIsoSelectionChanged();
                }
            }
        }

        public bool IsManualIsoVisible => IsManualSelect;
        public bool IsDistroDisplayVisible => IsManualSelect;
        public bool IsDownloadButtonVisible => !IsManualSelect && !IsDownloading;

        public DistroInfo? SelectedDistro
        {
            get => _selectedDistro;
            set
            {
                if (!SetProperty(ref _selectedDistro, value))
                    return;

                // Trocar a distro a baixar invalida uma ISO já baixada escolhida antes —
                // senão o botão "Instalar" seguiria usando o caminho antigo, de uma
                // distro diferente da exibida.
                if (SelectedDownloadedIso is not null)
                    SelectedDownloadedIso = null;

                OnPropertyChanged(nameof(IsSelectedDistroUnverifiable));
                _downloadIsoCommand.RaiseCanExecuteChanged();
                NotifyIsoSelectionChanged();
            }
        }

        /// <summary>A distro escolhida para baixar não tem hash de referência publicado pela
        /// fonte oficial (ver <see cref="DistroInfo.HasVerifiableArtifact"/>) — download
        /// automático fica desligado para ela, e a UI explica o motivo em vez de simplesmente
        /// esconder o botão sem contexto.</summary>
        public bool IsSelectedDistroUnverifiable => SelectedDistro is { HasVerifiableArtifact: false };

        public string? ManualIsoPath
        {
            get => _manualIsoPath;
            private set => SetProperty(ref _manualIsoPath, value);
        }

        /// <summary>Distro exibida: detectada na seleção manual, da ISO já baixada
        /// escolhida, ou a próxima a ser baixada.</summary>
        public DistroInfo? DisplayedDistro => IsManualSelect ? _detectedDistro : (SelectedDownloadedIso?.Distro ?? SelectedDistro);

        /// <summary>True assim que há um caminho de ISO resolvido, pronto pra instalação —
        /// independente de ter vindo de download novo, ISO já baixada ou seleção manual.</summary>
        public bool IsIsoReadyForInstall => !string.IsNullOrWhiteSpace(ResolvedIsoPath);

        /// <summary>Só a build com um mecanismo validado de ponta a ponta (ver <see
        /// cref="DistroInfo.UnattendedInstall"/>) pode oferecer o toggle — pra qualquer outra
        /// distro o wizard só prepara o boot até o instalador nativo, sem opção de ligar o que
        /// nunca foi testado.</summary>
        public bool IsAutoinstallToggleVisible => DisplayedDistro?.SupportsUnattendedInstall ?? false;

        /// <summary>Se a instalação desatendida deve rodar. Sempre false quando a distro não
        /// tem mecanismo validado, mesmo que o usuário tenha ligado o toggle antes de trocar
        /// de distro. Qual mecanismo será usado é decidido em <see cref="ActiveMechanism"/>.</summary>
        public bool IsAutoinstallActive => IsAutoinstallToggleVisible && _useAutoinstall;

        /// <summary>O mecanismo a usar de fato nesta instalação, ou <see
        /// cref="UnattendedInstallMechanism.None"/> quando o usuário optou por não automatizar
        /// (ou a distro não suporta) — o que faz o boot parar no instalador interativo.</summary>
        public UnattendedInstallMechanism ActiveMechanism => IsAutoinstallActive
            ? DisplayedDistro!.UnattendedInstall
            : UnattendedInstallMechanism.None;

        public bool UseAutoinstall
        {
            get => _useAutoinstall;
            set
            {
                if (!SetProperty(ref _useAutoinstall, value))
                    return;

                OnPropertyChanged(nameof(IsAutoinstallActive));
                OnPropertyChanged(nameof(SelectedIsoStatusText));
                OnPropertyChanged(nameof(IsAutoinstallVersionWarningVisible));
            }
        }

        /// <summary>Só ISO selecionada manualmente pode disparar isso — download pelo catálogo
        /// e "ISOs já baixadas" sempre vêm do mesmo link direto da versão testada, então não há
        /// incerteza nesses dois casos. Só aparece se o autoinstall está de fato ativo: uma
        /// versão incerta que não vai rodar autoinstall mesmo (toggle desligado) não precisa
        /// de alerta nenhum.</summary>
        public bool IsAutoinstallVersionWarningVisible =>
            IsManualSelect && _isManualIsoVersionUncertain && IsAutoinstallActive;

        /// <summary>Confirmação única, ao final da seção, de qual ISO será de fato usada —
        /// sem ela o usuário via dois campos (lista de baixadas + escolher pra baixar) sem
        /// indicação de qual dos dois estava valendo.</summary>
        public string SelectedIsoStatusText
        {
            get
            {
                var loc = LocalizationManager.Instance;

                if (ResolvedIsoPath is not { } path)
                    return loc["Wizard_NoIsoSelectedForInstall"];

                string name = DisplayedDistro?.Name ?? Path.GetFileName(path);
                string key = IsAutoinstallActive ? "Wizard_SelectedIsoForInstall" : "Wizard_SelectedIsoManualInstallOnly";
                return loc.Format(key, name);
            }
        }

        public bool IsDownloading
        {
            get => _isDownloading;
            private set
            {
                if (SetProperty(ref _isDownloading, value))
                {
                    OnPropertyChanged(nameof(IsDownloadButtonVisible));
                    _downloadIsoCommand.RaiseCanExecuteChanged();
                    _cancelDownloadCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public double DownloadPercent
        {
            get => _downloadPercent;
            private set => SetProperty(ref _downloadPercent, value);
        }

        /// <summary>True quando o servidor não informou o tamanho total da ISO —
        /// a barra de progresso não tem como mostrar percentual real nesse caso.</summary>
        public bool IsDownloadIndeterminate
        {
            get => _isDownloadIndeterminate;
            private set => SetProperty(ref _isDownloadIndeterminate, value);
        }

        public string DownloadStatusText
        {
            get => _downloadStatusText;
            private set => SetProperty(ref _downloadStatusText, value);
        }

        /// <summary>ISO local selecionada manualmente sendo verificada contra o hash do
        /// catálogo — só uma leitura completa do arquivo prova a integridade dele, então isto
        /// pode levar dezenas de segundos numa ISO de vários GB (§1.7).</summary>
        public bool IsVerifyingManualIso
        {
            get => _isVerifyingManualIso;
            private set => SetProperty(ref _isVerifyingManualIso, value);
        }

        public double ManualVerificationPercent
        {
            get => _manualVerificationPercent;
            private set => SetProperty(ref _manualVerificationPercent, value);
        }

        /// <summary>Caminho final da ISO pronta para uso (baixada ou selecionada manualmente).</summary>
        public string? ResolvedIsoPath { get; private set; }

        public ICommand DownloadIsoCommand => _downloadIsoCommand;
        public ICommand CancelDownloadCommand => _cancelDownloadCommand;
        public ICommand DownloadAnotherIsoCommand => _downloadAnotherIsoCommand;
        public ICommand UseDownloadedIsoCommand => _useDownloadedIsoCommand;

        public event Action<string, string, bool>? Notify;

        /// <summary>
        /// Entrada vinda do catálogo ("instalar agora"): deixa esta tela já apontada para
        /// <paramref name="distro"/>. Se a ISO dela já foi baixada, é essa que fica
        /// selecionada — baixar de novo seriam vários GB por nada; caso contrário abre o
        /// seletor de download já nela, pronto pro usuário só clicar em baixar.
        /// </summary>
        public void PrepareForDistro(DistroInfo distro)
        {
            ArgumentNullException.ThrowIfNull(distro);

            IsManualSelect = false;

            // Antes de SelectedDownloadedIso, nunca depois: trocar a distro limpa a ISO
            // baixada selecionada (ver o setter de SelectedDistro).
            SelectedDistro = distro;

            var downloaded = DownloadedIsos.FirstOrDefault(iso => iso.Distro?.Id == distro.Id);
            SelectedDownloadedIso = downloaded;
            IsChoosingNewDownload = downloaded is null;
        }

        /// <summary>
        /// Chamado pela View após o usuário escolher um arquivo no diálogo de seleção. Quando a
        /// distro detectada tem hash de referência (<see cref="DistroInfo.HasVerifiableArtifact"/>),
        /// o arquivo é lido por completo e verificado antes de ser aceito — reaproveitar um
        /// arquivo local não dispensa a verificação (artifact-integrity spec). Uma distro
        /// reconhecida mas sem hash de referência é aceita como não verificada, com aviso.
        /// </summary>
        public async Task SelectManualIsoAsync(string path)
        {
            var loc = LocalizationManager.Instance;

            if (!IsValidIso(path))
            {
                Notify?.Invoke(loc["Wizard_IsoInvalidTitle"], loc["Wizard_IsoInvalidMessage"], true);
                RejectManualIso();
                return;
            }

            var detection = _detectionService.Detect(path);
            if (detection.Distro.Id == DistroDetectionService.UnknownDistroId)
            {
                // Uma distro que o LinuxHub não reconhece não tem template de autoinstall/GRUB
                // associado — deixar passar geraria um install.conf pra uma distro que o
                // instalador não sabe preparar. Bloquear aqui, e não só na hora de instalar,
                // evita o usuário preencher todo o wizard pra descobrir isso só no final.
                Notify?.Invoke(loc["Wizard_UnknownDistroTitle"], loc["Wizard_UnknownDistroMessage"], true);
                RejectManualIso();
                return;
            }

            if (detection.Distro.HasVerifiableArtifact)
            {
                var outcome = await VerifyManualIsoAsync(path, detection.Distro);
                if (!outcome.IsVerified)
                {
                    // Rejeita a seleção, mas nunca apaga o arquivo do usuário — a ISO é dele,
                    // não do app; só o que o app baixou (IsoDownloadService) é apagado em falha.
                    Notify?.Invoke(
                        loc["Wizard_IsoVerificationFailedTitle"],
                        loc.Format("Wizard_IsoVerificationFailedMessage", detection.Distro.Name),
                        true);
                    RejectManualIso();
                    return;
                }
            }
            else
            {
                // Aviso, não bloqueio: a spec exige informar a ausência de hash de referência,
                // não recusar a seleção. SupportsUnattendedInstall já fica false para esta
                // distro (DistroInfo), então nenhum mecanismo de instalação desatendida chega
                // a ser oferecido para um arquivo não verificado.
                Notify?.Invoke(
                    loc["Wizard_IsoNoReferenceHashTitle"],
                    loc.Format("Wizard_IsoNoReferenceHashMessage", detection.Distro.Name),
                    false);
            }

            ManualIsoPath = path;
            ResolvedIsoPath = path;
            _detectedDistro = detection.Distro;
            // Não bloqueia a seleção nem desliga o toggle — só sinaliza que essa versão pode
            // não ser a testada, pro alerta de versão incerta aparecer se o autoinstall
            // continuar ligado.
            _isManualIsoVersionUncertain = !detection.IsExpectedVersion;
            NotifyIsoSelectionChanged();
        }

        private async Task<ArtifactVerificationResult> VerifyManualIsoAsync(string path, DistroInfo distro)
        {
            IsVerifyingManualIso = true;
            ManualVerificationPercent = 0;

            try
            {
                var progress = new Progress<double>(p => ManualVerificationPercent = p);
                return await _artifactVerifier.VerifyFileAsync(
                    path, distro.Sha256, distro.SizeBytes, progress, CancellationToken.None);
            }
            finally
            {
                IsVerifyingManualIso = false;
            }
        }

        private void RejectManualIso()
        {
            ManualIsoPath = null;
            ResolvedIsoPath = null;
            _detectedDistro = null;
            _isManualIsoVersionUncertain = false;
            NotifyIsoSelectionChanged();
        }

        private void UseDownloadedIso()
        {
            if (SelectedDownloadedIso is null && DownloadedIsos.Count > 0)
                SelectedDownloadedIso = DownloadedIsos[0];

            IsChoosingNewDownload = false;
        }

        private void NotifyIsoSelectionChanged()
        {
            OnPropertyChanged(nameof(DisplayedDistro));
            OnPropertyChanged(nameof(IsIsoReadyForInstall));
            OnPropertyChanged(nameof(SelectedIsoStatusText));
            OnPropertyChanged(nameof(IsAutoinstallToggleVisible));
            OnPropertyChanged(nameof(IsAutoinstallActive));
            OnPropertyChanged(nameof(IsAutoinstallVersionWarningVisible));
        }

        private static bool IsValidIso(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;

            return new FileInfo(path).Length > MinimumIsoSizeBytes;
        }

        private async Task DownloadAsync()
        {
            if (SelectedDistro is not { } distro)
                return;

            var loc = LocalizationManager.Instance;

            IsDownloading = true;
            DownloadPercent = 0;
            IsDownloadIndeterminate = false;
            DownloadStatusText = loc["Wizard_DownloadStarting"];

            _downloadCts = new CancellationTokenSource();
            var progress = new Progress<IsoDownloadProgress>(p =>
            {
                DownloadPercent = p.PercentComplete ?? 0;
                IsDownloadIndeterminate = p.TotalBytes is null;

                var speedText = $"{FormatBytes(p.BytesPerSecond)}/s";

                if (p.TotalBytes is not { } totalBytes)
                {
                    DownloadStatusText = loc.Format(
                        "Wizard_DownloadProgressUnknownTotal",
                        FormatBytes(p.BytesReceived),
                        speedText);
                    return;
                }

                var remaining = p.BytesPerSecond > 0
                    ? TimeSpan.FromSeconds((totalBytes - p.BytesReceived) / p.BytesPerSecond)
                    : TimeSpan.Zero;

                DownloadStatusText = loc.Format(
                    "Wizard_DownloadProgress",
                    FormatBytes(p.BytesReceived),
                    FormatBytes(totalBytes),
                    speedText,
                    FormatDuration(remaining));
            });

            try
            {
                var path = await _downloadService.DownloadAsync(distro, progress, _downloadCts.Token);

                // Substitui uma entrada antiga da mesma distro (baixada de novo) em vez de
                // duplicar, e seleciona o resultado — é isso que deixa a ISO recém-baixada
                // pronta pra instalar sem o usuário precisar ir em seleção manual procurá-la.
                var existing = DownloadedIsos.FirstOrDefault(iso => iso.Path == path);
                if (existing is not null)
                    DownloadedIsos.Remove(existing);

                var downloaded = new DownloadedIso(path, distro, DateTime.UtcNow);
                DownloadedIsos.Insert(0, downloaded);
                OnPropertyChanged(nameof(HasDownloadedIsos));

                // Volta pro modo "lista de baixadas" com a recém-baixada selecionada —
                // sem isso o usuário ficaria vendo o combo de "escolher distro pra baixar"
                // depois de já ter baixado, sem indicação clara do que instalar.
                IsChoosingNewDownload = false;
                SelectedDownloadedIso = downloaded;

                Notify?.Invoke(loc["Wizard_InstallSuccessTitle"], loc.Format("Wizard_DownloadCompleted", path), false);
            }
            catch (OperationCanceledException)
            {
                Notify?.Invoke(loc["Wizard_InstallSuccessTitle"], loc["Wizard_DownloadCancelled"], false);
            }
            catch (ArtifactVerificationException)
            {
                // Mensagem localizada, não ex.Message (diagnóstico em inglês, não voltado ao
                // usuário — ver constitution §4.2). O arquivo já foi apagado por quem lançou.
                Notify?.Invoke(
                    loc["Wizard_IsoVerificationFailedTitle"],
                    loc.Format("Wizard_IsoVerificationFailedMessage", distro.Name),
                    true);
            }
            catch (Exception ex)
            {
                Notify?.Invoke(loc["Wizard_DownloadErrorTitle"], ex.Message, true);
            }
            finally
            {
                IsDownloading = false;
                _downloadCts = null;
            }
        }

        private static string FormatBytes(double bytes)
        {
            const double Mb = 1024 * 1024;
            const double Gb = Mb * 1024;

            return bytes >= Gb ? $"{bytes / Gb:n1} GB" : $"{Math.Max(0, bytes) / Mb:n0} MB";
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalHours >= 1)
                return $"{(int)duration.TotalHours}h {duration.Minutes}m";
            if (duration.TotalMinutes >= 1)
                return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";

            return $"{Math.Max(1, duration.Seconds)}s";
        }
    }
}
