using System.Linq;
using System.Windows.Input;
using LinuxHub.Common.Data;
using LinuxHub.Common.Models;
using LinuxHub.Common.Mvvm;

namespace LinuxHub.Features.Catalog.ViewModels
{
    /// <summary>
    /// Navegação Catalog → Detalhe → Imagem em tela cheia é dirigida por estado
    /// (não por Window/Frame): a View troca o que exibe conforme
    /// <see cref="SelectedDistroDetail"/>/<see cref="FullscreenImagePath"/> mudam.
    /// Ver design.md do change modernize-ui-and-localization.
    /// </summary>
    public class CatalogViewModel : ObservableObject
    {
        private DistroDetailViewModel? _selectedDistroDetail;
        private string? _fullscreenImagePath;

        public CatalogViewModel()
        {
            // Uma entrada desabilitada (DistroInfo.IsEnabled) permanece no catálogo de dados —
            // só não é oferecida na navegação. Ver o comentário de cada entrada em
            // DistroCatalog para o motivo específico.
            Distros = DistroCatalog.All.Where(distro => distro.IsEnabled).ToList();
            OpenDistroCommand = new RelayCommand(param => OpenDistro((DistroInfo)param!));
            CloseFullscreenCommand = new RelayCommand(() => FullscreenImagePath = null);
        }

        public IReadOnlyList<DistroInfo> Distros { get; }

        public DistroDetailViewModel? SelectedDistroDetail
        {
            get => _selectedDistroDetail;
            private set => SetProperty(ref _selectedDistroDetail, value);
        }

        public string? FullscreenImagePath
        {
            get => _fullscreenImagePath;
            private set => SetProperty(ref _fullscreenImagePath, value);
        }

        public ICommand OpenDistroCommand { get; }
        public ICommand CloseFullscreenCommand { get; }

        /// <summary>Repassa o "instalar agora" do detalhe para fora da feature — quem navega
        /// entre catálogo e instalação é o shell, não o catálogo.</summary>
        public event Action<DistroInfo>? InstallRequested;

        private void OpenDistro(DistroInfo distro)
        {
            SelectedDistroDetail?.Dispose();

            var detail = new DistroDetailViewModel(distro);
            detail.OpenImageRequested += path => FullscreenImagePath = path;
            detail.InstallRequested += requested => InstallRequested?.Invoke(requested);
            detail.BackRequested += () =>
            {
                detail.Dispose();
                SelectedDistroDetail = null;
            };
            SelectedDistroDetail = detail;
        }
    }
}
