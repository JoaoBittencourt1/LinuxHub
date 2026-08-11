using System.Linq;
using LinuxHub.Common.Data;
using LinuxHub.Common.Models;
using Xunit;

namespace LinuxHub.Tests.Common.Data
{
    /// <summary>
    /// Função pura (D18: sem I/O, sem interface) que decide o que do documento remoto já
    /// verificado entra no catálogo efetivo. A regra que mais importa: assets locais
    /// (ImagePath/CarouselImages) nunca vêm do documento remoto (D14) — são recursos pack://
    /// compilados no executável, e um documento remoto malicioso ou só desatualizado não pode
    /// apontar pra um recurso que não existe.
    /// </summary>
    public class CatalogMergeTests
    {
        private static readonly DistroInfo LocalUbuntu = new()
        {
            Id = "ubuntu",
            Name = "Ubuntu",
            ImagePath = "pack://application:,,,/Assets/Images/Ubuntu.png",
            CarouselImages = new[] { "pack://application:,,,/Assets/Images/Ubuntu/ubuntu1.jpg" },
        };

        private static RemoteDistroEntry ValidUbuntuEntry() => new()
        {
            Id = "ubuntu",
            Name = "Ubuntu (remoto)",
            Family = "Debian",
            Version = "24.04.5",
            CreatedYear = "2004",
            BeginnerRating = 5,
            IsTested = true,
            IsEnabled = true,
            UnattendedInstall = "Subiquity",
            LiveSession = "Casper",
            DownloadLink = "https://ubuntu.com/download/desktop",
            DirectDownloadLink = "https://releases.ubuntu.com/24.04/ubuntu-24.04.5-desktop-amd64.iso",
            Sha256 = new string('a', 64),
            SizeBytes = 123,
        };

        [Fact]
        public void Merge_KnownId_TakesMetadataFromRemoteAndAssetsFromLocal()
        {
            var remote = new RemoteCatalogDocument { SchemaVersion = 1, Distributions = { ValidUbuntuEntry() } };

            var merged = CatalogMerge.Merge(remote, new[] { LocalUbuntu });

            Assert.NotNull(merged);
            var ubuntu = Assert.Single(merged!);
            Assert.Equal("Ubuntu (remoto)", ubuntu.Name);
            Assert.Equal("24.04.5", ubuntu.Version);
            Assert.Equal(new string('a', 64), ubuntu.Sha256);
            // Os dois campos de asset vêm do LOCAL, nunca do documento remoto — mesmo que o
            // documento remoto não declare nada parecido com um campo de imagem, este é o
            // contrato que impede alguém de adicionar um amanhã e ele ser aceito por engano.
            Assert.Equal(LocalUbuntu.ImagePath, ubuntu.ImagePath);
            Assert.Equal(LocalUbuntu.CarouselImages, ubuntu.CarouselImages);
        }

        /// <summary>Uma distro nova anunciada só pelo catálogo remoto não tem assets compilados
        /// no executável — é ignorada, não criada "sem imagem".</summary>
        [Fact]
        public void Merge_UnknownId_IsIgnored()
        {
            var unknown = ValidUbuntuEntry();
            unknown.Id = "distro-nova-que-o-exe-nao-conhece";
            var remote = new RemoteCatalogDocument { SchemaVersion = 1, Distributions = { unknown } };

            var merged = CatalogMerge.Merge(remote, new[] { LocalUbuntu });

            Assert.Empty(merged!);
        }

        [Fact]
        public void Merge_InvalidUnattendedInstallEnum_RejectsTheEntireDocument()
        {
            var entry = ValidUbuntuEntry();
            entry.UnattendedInstall = "NotARealMechanism";
            var remote = new RemoteCatalogDocument { SchemaVersion = 1, Distributions = { entry } };

            var merged = CatalogMerge.Merge(remote, new[] { LocalUbuntu });

            Assert.Null(merged);
        }

        [Fact]
        public void Merge_InvalidLiveSessionEnum_RejectsTheEntireDocument()
        {
            var entry = ValidUbuntuEntry();
            entry.LiveSession = "NotARealFamily";
            var remote = new RemoteCatalogDocument { SchemaVersion = 1, Distributions = { entry } };

            var merged = CatalogMerge.Merge(remote, new[] { LocalUbuntu });

            Assert.Null(merged);
        }

        [Fact]
        public void Merge_EmptyDistributions_ReturnsEmptyList()
        {
            var remote = new RemoteCatalogDocument { SchemaVersion = 1, Distributions = { } };

            var merged = CatalogMerge.Merge(remote, new[] { LocalUbuntu });

            Assert.NotNull(merged);
            Assert.Empty(merged!);
        }

        /// <summary>
        /// Teste de paridade de forma (D9): projeta cada entrada real do catálogo embarcado
        /// como se tivesse vindo do documento remoto e roda pelo merge de verdade. Se um campo
        /// novo for adicionado a <see cref="DistroInfo"/> sem o par correspondente em
        /// <see cref="RemoteDistroEntry"/>/<see cref="CatalogMerge"/>, este teste é o que
        /// detecta a divergência — sem ele, o campo simplesmente nunca seria atualizável por
        /// catálogo remoto e ninguém notaria até precisar.
        /// </summary>
        [Fact]
        public void Merge_EveryFallbackEntryRoundTripsThroughARemoteRepresentation()
        {
            var remoteEntries = DistroCatalog.Fallback.Select(distro => new RemoteDistroEntry
            {
                Id = distro.Id,
                Name = distro.Name,
                Family = distro.Family,
                Version = distro.Version,
                CreatedYear = distro.CreatedYear,
                BeginnerRating = distro.BeginnerRating,
                IsTested = distro.IsTested,
                IsEnabled = distro.IsEnabled,
                UnattendedInstall = distro.UnattendedInstall.ToString(),
                LiveSession = distro.LiveSession.ToString(),
                DownloadLink = distro.DownloadLink,
                DirectDownloadLink = distro.DirectDownloadLink,
                Sha256 = distro.Sha256,
                SizeBytes = distro.SizeBytes,
            }).ToList();
            var remote = new RemoteCatalogDocument { SchemaVersion = 1, Distributions = remoteEntries };

            var merged = CatalogMerge.Merge(remote, DistroCatalog.Fallback);

            Assert.NotNull(merged);
            Assert.Equal(DistroCatalog.Fallback.Count, merged!.Count);
            foreach (var (original, roundTripped) in DistroCatalog.Fallback.Zip(merged))
            {
                Assert.Equal(original.Id, roundTripped.Id);
                Assert.Equal(original.Name, roundTripped.Name);
                Assert.Equal(original.Version, roundTripped.Version);
                Assert.Equal(original.IsEnabled, roundTripped.IsEnabled);
                Assert.Equal(original.UnattendedInstall, roundTripped.UnattendedInstall);
                Assert.Equal(original.LiveSession, roundTripped.LiveSession);
                Assert.Equal(original.Sha256, roundTripped.Sha256);
                Assert.Equal(original.SizeBytes, roundTripped.SizeBytes);
                Assert.Equal(original.ImagePath, roundTripped.ImagePath);
            }
        }
    }
}
