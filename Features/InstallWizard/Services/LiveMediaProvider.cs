using System.IO;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Resolução por dois caminhos, nesta ordem — nunca adivinha (§6.1):
    /// 1. Variável de ambiente <see cref="EnvironmentVariableName"/>, para apontar
    ///    explicitamente para uma ISO construída localmente (fluxo de teste em VM antes da
    ///    task 1.9 existir de verdade).
    /// 2. <c>%ProgramData%\LinuxHub\LiveMedia\linuxhub-live.iso</c> — local fixo e
    ///    documentado, fora do repositório (a ISO tem centenas de MB, não é asset commitado
    ///    como <see cref="GrubAssetProvider"/>).
    /// </summary>
    public sealed class LiveMediaProvider : ILiveMediaProvider
    {
        internal const string EnvironmentVariableName = "LINUXHUB_LIVE_MEDIA_ISO";
        internal const string DefaultFileName = "linuxhub-live.iso";

        private readonly Func<string?> _getEnvironmentOverride;
        private readonly string _defaultPath;

        public LiveMediaProvider() : this(
            () => Environment.GetEnvironmentVariable(EnvironmentVariableName),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "LinuxHub", "LiveMedia", DefaultFileName))
        {
        }

        internal LiveMediaProvider(Func<string?> getEnvironmentOverride, string defaultPath)
        {
            _getEnvironmentOverride = getEnvironmentOverride ?? throw new ArgumentNullException(nameof(getEnvironmentOverride));
            _defaultPath = defaultPath ?? throw new ArgumentNullException(nameof(defaultPath));
        }

        public string GetIsoPath()
        {
            string? overridePath = _getEnvironmentOverride();
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                if (!File.Exists(overridePath))
                {
                    throw new FileNotFoundException(
                        $"A variável de ambiente {EnvironmentVariableName} aponta para " +
                        $"'{overridePath}', mas o arquivo não existe.", overridePath);
                }

                return overridePath;
            }

            if (!File.Exists(_defaultPath))
            {
                throw new FileNotFoundException(
                    $"Mídia live não encontrada em '{_defaultPath}'. Construa-a com " +
                    "live-media/build/build-live-media.sh (requer Linux/WSL — debootstrap, " +
                    "squashfs-tools, grub-efi-amd64-bin, xorriso) e copie " +
                    "out/linuxhub-live.iso para esse caminho, ou aponte a variável de " +
                    $"ambiente {EnvironmentVariableName} para a ISO construída.", _defaultPath);
            }

            return _defaultPath;
        }
    }
}
