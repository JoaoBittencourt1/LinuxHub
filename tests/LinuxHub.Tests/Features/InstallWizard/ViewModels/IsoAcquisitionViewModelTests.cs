using System.IO;
using LinuxHub.Common.Data;
using LinuxHub.Common.Models;
using LinuxHub.Features.InstallWizard.Services;
using LinuxHub.Features.InstallWizard.ViewModels;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.ViewModels
{
    /// <summary>
    /// Cobre a entrada vinda do catálogo ("instalar agora") e a visibilidade do toggle de
    /// instalação automática, que só existe para a distro validada de ponta a ponta.
    /// </summary>
    public class IsoAcquisitionViewModelTests
    {
        private sealed class FakeIsoDownloadService : IIsoDownloadService
        {
            public Task<string> DownloadAsync(
                DistroInfo distro, IProgress<IsoDownloadProgress> progress, CancellationToken cancellationToken) =>
                Task.FromResult($@"C:\isos\{distro.Id}.iso");
        }

        private sealed class FakeDistroDetectionService : IDistroDetectionService
        {
            public DistroDetectionResult Detect(string isoPath) =>
                new(new DistroInfo { Name = "Ubuntu" }, IsExpectedVersion: true);
        }

        private sealed class FakeDownloadedIsoRepository : IDownloadedIsoRepository
        {
            public IReadOnlyList<DownloadedIso> Isos { get; init; } = Array.Empty<DownloadedIso>();

            public IReadOnlyList<DownloadedIso> GetAll() => Isos;
        }

        /// <summary>Devolve o resultado configurado sem tocar em disco — os testes de
        /// verificação real de arquivo (hash/tamanho calculados de verdade) vivem em
        /// <c>ArtifactVerifierTests</c>; aqui o que importa é como a ViewModel reage a cada
        /// resultado possível.</summary>
        private sealed class FakeArtifactVerifier : IArtifactVerifier
        {
            public ArtifactVerificationResult ResultToReturn { get; set; } = ArtifactVerificationResult.Verified();
            public string? LastVerifiedPath { get; private set; }

            public Task<ArtifactVerificationResult> VerifyFileAsync(
                string filePath, string expectedSha256, long expectedSizeBytes,
                IProgress<double>? progress, CancellationToken cancellationToken)
            {
                LastVerifiedPath = filePath;
                progress?.Report(100);
                return Task.FromResult(ResultToReturn);
            }
        }

        /// <summary>Detecta sempre a distro configurada, sem depender do nome do arquivo — os
        /// testes de detecção por nome de arquivo vivem em <c>DistroCatalogTests</c>.</summary>
        private sealed class ConfigurableDistroDetectionService : IDistroDetectionService
        {
            public DistroInfo DistroToReturn { get; set; } = new() { Id = "ubuntu", Name = "Ubuntu" };

            public DistroDetectionResult Detect(string isoPath) => new(DistroToReturn, IsExpectedVersion: true);
        }

        private static DistroInfo Distro(string id) =>
            DistroCatalog.All.First(distro => distro.Id == id);

        private static IsoAcquisitionViewModel BuildViewModel(params DownloadedIso[] downloaded) =>
            new(new FakeIsoDownloadService(),
                new FakeDistroDetectionService(),
                new FakeDownloadedIsoRepository { Isos = downloaded },
                new FakeArtifactVerifier());

        private static string CreateTempFileLargerThanMinimumIsoSize()
        {
            string path = Path.Combine(Path.GetTempPath(), $"linuxhub-manual-iso-{Guid.NewGuid():N}.iso");
            using (var stream = File.Create(path))
                stream.SetLength(800L * 1024 * 1024);
            return path;
        }

        /// <summary>Com a ISO já em disco, "instalar agora" não pode mandar o usuário baixar
        /// vários GB de novo — a instalação já pode começar dali.</summary>
        [Fact]
        public void PrepareForDistro_WithIsoAlreadyDownloaded_SelectsItForInstall()
        {
            var mint = Distro("mint");
            var downloaded = new DownloadedIso(@"C:\isos\mint.iso", mint, DateTime.UtcNow);
            var vm = BuildViewModel(downloaded);

            vm.PrepareForDistro(mint);

            Assert.Same(downloaded, vm.SelectedDownloadedIso);
            Assert.Equal(@"C:\isos\mint.iso", vm.ResolvedIsoPath);
            Assert.True(vm.IsIsoReadyForInstall);
            Assert.Same(mint, vm.DisplayedDistro);
        }

        /// <summary>Sem ISO dessa distro em disco, o wizard abre no seletor de download já
        /// apontado pra ela — não na lista de outras ISOs já baixadas.</summary>
        [Fact]
        public void PrepareForDistro_WithoutIso_OpensTheDownloadPickerOnIt()
        {
            var fedora = Distro("fedora");
            var vm = BuildViewModel(new DownloadedIso(@"C:\isos\ubuntu.iso", Distro("ubuntu"), DateTime.UtcNow));

            vm.PrepareForDistro(fedora);

            Assert.Same(fedora, vm.SelectedDistro);
            Assert.Null(vm.SelectedDownloadedIso);
            Assert.False(vm.IsIsoReadyForInstall);
            Assert.True(vm.IsDistroPickerVisible);
            Assert.False(vm.IsDownloadedIsosVisible);
        }

        [Fact]
        public void PrepareForDistro_LeavesManualSelection()
        {
            var vm = BuildViewModel();
            vm.IsManualSelect = true;

            vm.PrepareForDistro(Distro("ubuntu"));

            Assert.False(vm.IsManualSelect);
        }

        /// <summary>O toggle de instalação automática só pode aparecer nas distros que
        /// declaram um mecanismo validado de ponta a ponta — as demais nem oferecem a opção.</summary>
        [Theory]
        [InlineData("ubuntu", true)]
        [InlineData("mint", false)]
        [InlineData("xubuntu", false)]
        [InlineData("arch", false)]
        public void AutoinstallToggle_IsVisibleOnlyForDistrosWithAMechanism(
            string distroId, bool expected)
        {
            var vm = BuildViewModel();

            vm.PrepareForDistro(Distro(distroId));

            Assert.Equal(expected, vm.IsAutoinstallToggleVisible);
            Assert.Equal(expected, vm.IsAutoinstallActive);
        }

        /// <summary>Ligar o toggle não basta: o mecanismo ativo tem que ser o da distro, senão
        /// o wizard gera a configuração de um instalador para outro.</summary>
        [Theory]
        [InlineData("ubuntu", UnattendedInstallMechanism.Subiquity)]
        [InlineData("mint", UnattendedInstallMechanism.None)]
        [InlineData("arch", UnattendedInstallMechanism.None)]
        public void ActiveMechanism_FollowsTheSelectedDistro(
            string distroId, UnattendedInstallMechanism expected)
        {
            var vm = BuildViewModel();

            vm.PrepareForDistro(Distro(distroId));

            Assert.Equal(expected, vm.ActiveMechanism);
        }

        /// <summary>Kubuntu, EndeavourOS e Kali estão desabilitados por ora (DistroInfo.IsEnabled
        /// — ver DistroCatalogTests) e não podem aparecer no seletor de download, mesmo que o
        /// usuário já os tenha selecionado antes via <see cref="IsoAcquisitionViewModel.PrepareForDistro"/>.</summary>
        [Fact]
        public void Distros_ExcludesDisabledEntries()
        {
            var vm = BuildViewModel();

            Assert.DoesNotContain(vm.Distros, distro => !distro.IsEnabled);
        }

        /// <summary>Uma distro sem hash de referência (Kubuntu, EndeavourOS — ver
        /// DistroCatalogTests) não pode ser baixada automaticamente, mesmo com tudo o mais
        /// preenchido — artifact-integrity spec: "Uma entrada sem esses campos SHALL NOT ser
        /// oferecida para download".</summary>
        [Fact]
        public void DownloadCommand_IsDisabledForADistroWithoutAVerifiableArtifact()
        {
            var vm = BuildViewModel();

            vm.SelectedDistro = Distro("kubuntu");
            Assert.False(vm.DownloadIsoCommand.CanExecute(null));
            Assert.True(vm.IsSelectedDistroUnverifiable);

            vm.SelectedDistro = Distro("ubuntu");
            Assert.True(vm.DownloadIsoCommand.CanExecute(null));
            Assert.False(vm.IsSelectedDistroUnverifiable);
        }

        /// <summary>ISO local cujo conteúdo bate com o hash do catálogo: aceita e marca pronta
        /// pra instalar, sem reportar nenhuma falha.</summary>
        [Fact]
        public async Task SelectManualIsoAsync_VerifiedArtifact_AcceptsTheFile()
        {
            var verifier = new FakeArtifactVerifier { ResultToReturn = ArtifactVerificationResult.Verified() };
            var detection = new ConfigurableDistroDetectionService { DistroToReturn = Distro("ubuntu") };
            var vm = new IsoAcquisitionViewModel(
                new FakeIsoDownloadService(), detection, new FakeDownloadedIsoRepository(), verifier)
            {
                IsManualSelect = true
            };

            string path = CreateTempFileLargerThanMinimumIsoSize();
            try
            {
                await vm.SelectManualIsoAsync(path);

                Assert.Equal(path, vm.ResolvedIsoPath);
                Assert.Equal(path, verifier.LastVerifiedPath);
                Assert.True(vm.IsIsoReadyForInstall);
                Assert.False(vm.IsVerifyingManualIso);
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>ISO local cujo hash não bate: rejeitada, sem apagar o arquivo do usuário —
        /// só o que o app baixou é apagado em falha (IsoDownloadService), nunca o que o usuário
        /// já tinha em disco.</summary>
        [Fact]
        public async Task SelectManualIsoAsync_HashMismatch_RejectsWithoutDeletingTheFile()
        {
            var verifier = new FakeArtifactVerifier { ResultToReturn = ArtifactVerificationResult.HashMismatch() };
            var detection = new ConfigurableDistroDetectionService { DistroToReturn = Distro("ubuntu") };
            var vm = new IsoAcquisitionViewModel(
                new FakeIsoDownloadService(), detection, new FakeDownloadedIsoRepository(), verifier)
            {
                IsManualSelect = true
            };

            string path = CreateTempFileLargerThanMinimumIsoSize();
            try
            {
                await vm.SelectManualIsoAsync(path);

                Assert.Null(vm.ResolvedIsoPath);
                Assert.Null(vm.ManualIsoPath);
                Assert.False(vm.IsIsoReadyForInstall);
                Assert.True(File.Exists(path), "o arquivo do usuário não pode ser apagado numa falha de verificação");
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>Uma distro reconhecida mas sem hash de referência publicado (Kubuntu,
        /// EndeavourOS) é aceita como não verificada, e o toggle de instalação automática
        /// permanece desligado — <see cref="DistroInfo.SupportsUnattendedInstall"/> já garante a
        /// segunda parte estruturalmente.</summary>
        [Fact]
        public async Task SelectManualIsoAsync_DistroWithoutReferenceHash_AcceptsAsUnverified()
        {
            var verifier = new FakeArtifactVerifier();
            var detection = new ConfigurableDistroDetectionService { DistroToReturn = Distro("endeavour") };
            var vm = new IsoAcquisitionViewModel(
                new FakeIsoDownloadService(), detection, new FakeDownloadedIsoRepository(), verifier)
            {
                // DisplayedDistro só lê _detectedDistro em modo manual — é o que a RadioButton
                // "Selecionar manualmente" liga antes do botão de busca ficar visível.
                IsManualSelect = true
            };

            string path = CreateTempFileLargerThanMinimumIsoSize();
            try
            {
                await vm.SelectManualIsoAsync(path);

                Assert.Equal(path, vm.ResolvedIsoPath);
                Assert.True(vm.IsIsoReadyForInstall);
                // O verificador nunca chega a ser chamado: sem hash de referência não há contra
                // o que verificar.
                Assert.Null(verifier.LastVerifiedPath);
                Assert.False(vm.IsAutoinstallToggleVisible);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
