using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LinuxHub.Common.Localization;
using LinuxHub.Common.Ui;
using LinuxHub.Features.Catalog.ViewModels;

namespace LinuxHub.Features.Catalog.Views
{
    /// <summary>
    /// Renderização do carrossel (imagem vs. vídeo) e liberação de mídia dependem
    /// diretamente de APIs de WPF (MediaElement, BitmapImage) — por isso ficam aqui
    /// no code-behind, não na ViewModel. Navegação (voltar/abrir imagem) já é
    /// resolvida pela CatalogViewModel via estado — esta view só cuida de mídia.
    /// </summary>
    public partial class DistroDetailView : UserControl
    {
        private readonly List<BitmapImage> _loadedImages = new();
        private MediaElement? _currentVideo;

        public DistroDetailView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            IsVisibleChanged += OnIsVisibleChanged;
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is true && DataContext is DistroDetailViewModel)
                PlayEntrance();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is DistroDetailViewModel oldVm)
            {
                oldVm.PropertyChanged -= ViewModel_PropertyChanged;
                oldVm.DownloadLinkOpenFailed -= OnDownloadLinkOpenFailed;
            }

            FreeCurrentMedia();

            if (e.NewValue is DistroDetailViewModel newVm)
            {
                newVm.PropertyChanged += ViewModel_PropertyChanged;
                newVm.DownloadLinkOpenFailed += OnDownloadLinkOpenFailed;
                UpdateCarousel(newVm);
                if (IsVisible)
                    PlayEntrance();
            }
        }

        private void PlayEntrance()
        {
            // Wait for layout so UntestedBar Visibility is resolved before skipping collapsed.
            Dispatcher.BeginInvoke(() =>
            {
                StaggeredReveal.Play(
                [
                    BackButton,
                    HeroCard,
                    MetaCard,
                    UntestedBar,
                    DescriptionCard,
                    ScreenshotsCard,
                    ActionsCard,
                ]);
            }, DispatcherPriority.Loaded);
        }

        private void OnDownloadLinkOpenFailed() =>
            MessageBox.Show(
                LocalizationManager.Instance["CatalogDetail_LinkOpenFailedMessage"],
                LocalizationManager.Instance["CatalogDetail_LinkOpenFailedTitle"],
                MessageBoxButton.OK,
                MessageBoxImage.Error);

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DistroDetailViewModel.CurrentCarouselItem))
                UpdateCarousel((DistroDetailViewModel)sender!);
        }

        private void UpdateCarousel(DistroDetailViewModel viewModel)
        {
            FreeCurrentMedia();

            var item = viewModel.CurrentCarouselItem;
            if (item is null)
                return;

            if (item.EndsWith(".mp4"))
            {
                var video = new MediaElement
                {
                    Source = new Uri(item, UriKind.RelativeOrAbsolute),
                    LoadedBehavior = MediaState.Manual,
                    UnloadedBehavior = MediaState.Stop,
                    Stretch = Stretch.Uniform
                };

                CarouselContent.Content = video;
                video.Play();
                _currentVideo = video;
                return;
            }

            var bmp = LoadBitmapSafe(item);
            _loadedImages.Add(bmp);

            var img = new Image
            {
                Source = bmp,
                Stretch = Stretch.Uniform,
                Cursor = Cursors.Hand
            };

            img.MouseLeftButtonUp += (_, _) => viewModel.OpenImageCommand.Execute(item);
            CarouselContent.Content = img;
        }

        private static BitmapImage LoadBitmapSafe(string path)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path, UriKind.RelativeOrAbsolute);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        private void FreeCurrentMedia()
        {
            if (_currentVideo != null)
            {
                try
                {
                    _currentVideo.Stop();
                    _currentVideo.Source = null;
                }
                catch (Exception)
                {
                    // WPF pode lançar se o MediaElement já foi descartado — parar/limpar é
                    // best-effort de liberação de recurso, não uma falha visível ao usuário.
                }
                _currentVideo = null;
            }

            foreach (var img in _loadedImages)
            {
                try
                {
                    img.StreamSource?.Dispose();
                }
                catch (Exception)
                {
                    // idem acima — best-effort de liberação de recurso.
                }
            }

            _loadedImages.Clear();

            if (CarouselContent != null)
                CarouselContent.Content = null;
        }
    }
}
