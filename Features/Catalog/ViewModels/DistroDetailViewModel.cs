using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Input;
using LinuxHub.Common.Localization;
using LinuxHub.Common.Models;
using LinuxHub.Common.Mvvm;

namespace LinuxHub.Features.Catalog.ViewModels
{
    public class DistroDetailViewModel : ObservableObject, IDisposable
    {
        private readonly string _descriptionKey;
        private readonly string _maintainerKey;
        private int _carouselIndex;

        public DistroDetailViewModel(DistroInfo distro)
        {
            ArgumentNullException.ThrowIfNull(distro);

            Name = distro.Name;
            _descriptionKey = distro.DescriptionKey;
            _maintainerKey = distro.MaintainerKey;
            CreatedYear = distro.CreatedYear;
            BeginnerRating = distro.BeginnerRating;
            IsUntested = !distro.IsTested;
            ImagePath = distro.ImagePath;
            DownloadLink = distro.DownloadLink;
            CarouselItems = distro.CarouselImages;

            // Description/Maintainer são resolvidos via LocalizationManager (não
            // hardcoded — ver constitution.md) e precisam avisar a View quando o
            // idioma muda, já que aqui não é um Binding direto de XAML como o loc:Loc.
            LocalizationManager.Instance.PropertyChanged += OnLanguageChanged;

            NextCommand = new RelayCommand(
                () => CarouselIndex = (CarouselIndex + 1) % CarouselItems.Count,
                () => CarouselItems.Count > 1);

            PrevCommand = new RelayCommand(
                () => CarouselIndex = (CarouselIndex - 1 + CarouselItems.Count) % CarouselItems.Count,
                () => CarouselItems.Count > 1);

            OpenImageCommand = new RelayCommand(path => OpenImageRequested?.Invoke((string)path!));
            OpenDownloadLinkCommand = new RelayCommand(OpenDownloadLink);
            BackCommand = new RelayCommand(() => BackRequested?.Invoke());
            InstallNowCommand = new RelayCommand(() => InstallRequested?.Invoke(distro));
        }

        public string Name { get; }
        public string Description => LocalizationManager.Instance[_descriptionKey];
        public string Maintainer => LocalizationManager.Instance[_maintainerKey];
        public string CreatedYear { get; }
        public int BeginnerRating { get; }

        /// <summary>Exposto na forma negada porque é assim que a View usa: o aviso aparece
        /// para quem NÃO foi testada, e é a maioria do catálogo. Ver
        /// <see cref="DistroInfo.IsTested"/>.</summary>
        public bool IsUntested { get; }
        public string ImagePath { get; }
        public string DownloadLink { get; }
        public IReadOnlyList<string> CarouselItems { get; }

        public int CarouselIndex
        {
            get => _carouselIndex;
            private set
            {
                if (SetProperty(ref _carouselIndex, value))
                    OnPropertyChanged(nameof(CurrentCarouselItem));
            }
        }

        public string? CurrentCarouselItem =>
            CarouselItems.Count > 0 ? CarouselItems[CarouselIndex] : null;

        public ICommand NextCommand { get; }
        public ICommand PrevCommand { get; }
        public ICommand OpenImageCommand { get; }
        public ICommand OpenDownloadLinkCommand { get; }
        public ICommand BackCommand { get; }
        public ICommand InstallNowCommand { get; }

        /// <summary>Pedido de abrir uma imagem do carrossel em tela cheia — a View decide como.</summary>
        public event Action<string>? OpenImageRequested;

        /// <summary>Pedido de voltar à janela principal — a View decide como.</summary>
        public event Action? BackRequested;

        /// <summary>Pedido de instalar esta distro — o shell decide como (troca para o painel
        /// de instalação e aponta o wizard pra ela).</summary>
        public event Action<DistroInfo>? InstallRequested;

        /// <summary>Falha ao abrir o link de download no navegador — a View decide como reportar.</summary>
        public event Action? DownloadLinkOpenFailed;

        private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(LocalizationManager.CurrentCulture))
                return;

            OnPropertyChanged(nameof(Description));
            OnPropertyChanged(nameof(Maintainer));
        }

        private void OpenDownloadLink()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = DownloadLink,
                    UseShellExecute = true
                });
            }
            catch (Exception)
            {
                DownloadLinkOpenFailed?.Invoke();
            }
        }

        /// <summary>
        /// Necessário porque o construtor assina LocalizationManager.Instance
        /// (singleton estático) — sem desinscrever, cada distro aberta vazaria uma
        /// ViewModel presa ao delegate de PropertyChanged do manager.
        /// </summary>
        public void Dispose() =>
            LocalizationManager.Instance.PropertyChanged -= OnLanguageChanged;
    }
}
