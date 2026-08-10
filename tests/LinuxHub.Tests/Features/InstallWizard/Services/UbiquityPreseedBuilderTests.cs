using System;
using System.Collections.Generic;
using System.Linq;
using LinuxHub.Features.InstallWizard.Models;
using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    public class UbiquityPreseedBuilderTests
    {
        private const string Hash = "$6$rounds=4096$abcd$xyz";

        private static InstallerConfig Config() => new()
        {
            Username = "joao",
            Password = "segredo",
            Hostname = "pc-do-joao",
            Locale = "pt_BR.UTF-8",
            Timezone = "America/Sao_Paulo",
            Keymap = "br",
            BootMode = "uefi",
        };

        private static DiskLayout Layout(bool isGpt, string seedGuid = "{aaaabbbb-cccc-dddd-eeee-ffff00001111}") =>
            new(
                Index: 0,
                SerialNumber: "S1",
                Model: "Disco",
                SizeBytes: 500L * 1024 * 1024 * 1024,
                IsGpt: isGpt,
                IsLargestDisk: true,
                IsSmallestDisk: true,
                Partitions:
                [
                    new PartitionLayout(3, 0, 100, "{gpt}", false, Guid: seedGuid),
                ],
                DiskSignatureHex: "1a2b3c4d",
                HasUniqueDiskSignature: true);

        private static string ValueOf(string preseed, string question) =>
            preseed.Split('\n')
                .Select(line => line.Split(' ', 4))
                .Where(parts => parts.Length == 4 && parts[1] == question)
                .Select(parts => parts[3])
                .Single();

        /// <summary>O preseed sobrevive ao reboot num disco que qualquer um pode ler — a senha
        /// em texto claro aqui ficaria exposta muito além da janela da instalação.</summary>
        [Fact]
        public void BuildPreseed_UsesTheHashAndNeverThePlainPassword()
        {
            string preseed = UbiquityPreseedBuilder.BuildPreseed(Config(), Hash, "d=/dev/sda", isReplaceMode: false);

            Assert.Equal(Hash, ValueOf(preseed, "passwd/user-password-crypted"));
            Assert.DoesNotContain("segredo", preseed);
        }

        [Fact]
        public void BuildPreseed_CarriesAccountAndSystemSettings()
        {
            string preseed = UbiquityPreseedBuilder.BuildPreseed(Config(), Hash, "d=/dev/sda", isReplaceMode: false);

            Assert.Equal("joao", ValueOf(preseed, "passwd/username"));
            Assert.Equal("pc-do-joao", ValueOf(preseed, "netcfg/get_hostname"));
            Assert.Equal("pt_BR.UTF-8", ValueOf(preseed, "debian-installer/locale"));
            Assert.Equal("America/Sao_Paulo", ValueOf(preseed, "time/zone"));
            Assert.Equal("br", ValueOf(preseed, "keyboard-configuration/layoutcode"));
        }

        /// <summary>Reiniciar sozinho é do Ubiquity (<c>ubiquity/reboot</c>); o
        /// <c>noprompt</c> do casper resolve outro estágio e não substitui este.</summary>
        [Fact]
        public void BuildPreseed_RebootsWithoutAskingForMediaRemoval() =>
            Assert.Equal("true", ValueOf(
                UbiquityPreseedBuilder.BuildPreseed(Config(), Hash, "d=/dev/sda", isReplaceMode: false),
                "ubiquity/reboot"));

        /// <summary>
        /// A página `prepare` trava a instalação se estas duas não vierem respondidas — no
        /// teste em VM de 2026-08-10 o Ubiquity emitiu <c>INPUT high ubiquity/use_nonfree</c>,
        /// renderizou o widget e ficou esperando. Nenhuma leitura estática do pacote tinha
        /// revelado essa parada, então o teste existe para que ela não volte em silêncio.
        /// </summary>
        [Fact]
        public void BuildPreseed_AnswersThePreparePageInsteadOfStopping()
        {
            string preseed = UbiquityPreseedBuilder.BuildPreseed(
                Config(), Hash, "d=/dev/sda", isReplaceMode: false);

            Assert.Equal("true", ValueOf(preseed, "ubiquity/use_nonfree"));

            // Baixar atualizações depende de rede, que não é garantida; sem isso o passo
            // trava por minutos quando ela falta.
            Assert.Equal("false", ValueOf(preseed, "ubiquity/download_updates"));
        }

        /// <summary>A receita vai num arquivo, não inline: ela tem espaços e quebras de linha,
        /// que não cabem num valor de debconf numa linha só.</summary>
        [Fact]
        public void BuildPreseed_PointsPartmanAtTheRecipeFile() =>
            Assert.Equal("/" + UbiquityPreseedBuilder.RecipeFileName, ValueOf(
                UbiquityPreseedBuilder.BuildPreseed(Config(), Hash, "d=/dev/sda", isReplaceMode: false),
                "partman-auto/expert_recipe_file"));

        /// <summary>
        /// EXPERIMENTAL (task 5b.6) — no dual-boot o `partman-auto/method` agora É emitido,
        /// porque o teste em VM de 2026-08-10 provou que ele é o interruptor do modo automático
        /// inteiro: sem a chave, o ubiquity fica em `auto_state = None` e nada é automatizado.
        ///
        /// Isso torna esta a asserção mais importante do arquivo: `method` sozinho significa
        /// DISCO INTEIRO. É a companhia do `init_automatically_partition` que o converte em
        /// "usar o espaço livre". Se alguma refatoração deixar cair a segunda chave e mantiver
        /// a primeira, o resultado não é uma pergunta ao usuário — é o Windows apagado, o
        /// mecanismo exato do incidente de 2026-08-05.
        /// </summary>
        [Fact]
        public void BuildPreseed_DualBoot_NeverSetsTheMethodWithoutTheFreeSpaceChoice()
        {
            string preseed = UbiquityPreseedBuilder.BuildPreseed(
                Config(), Hash, "d=/dev/sda", isReplaceMode: false);

            if (!preseed.Contains("partman-auto/method"))
                return;

            Assert.Equal(
                UbiquityPreseedBuilder.DualBootAutomaticPartitionChoice,
                ValueOf(preseed, "partman-auto/init_automatically_partition"));
        }

        /// <summary>No substituir o disco inteiro É o alvo, então `regular` é o correto — e é
        /// o que dispensa a tela de escolha por completo.</summary>
        [Fact]
        public void BuildPreseed_Replace_UsesTheWholeDiskMethod()
        {
            string preseed = UbiquityPreseedBuilder.BuildPreseed(
                Config(), Hash, "d=/dev/sda", isReplaceMode: true);

            Assert.Equal("regular", ValueOf(preseed, "partman-auto/method"));
            Assert.DoesNotContain("biggest_free", preseed);
        }

        /// <summary>Uma pergunta respondida com vazio é marcada como já vista, e o instalador
        /// PULA a etapa em vez de perguntar — pior que não respondê-la.</summary>
        [Fact]
        public void BuildPreseed_OmitsQuestionsWithoutAValue()
        {
            var config = Config();
            config.Timezone = string.Empty;

            string preseed = UbiquityPreseedBuilder.BuildPreseed(config, Hash, "d=/dev/sda", isReplaceMode: false);

            Assert.DoesNotContain("time/zone", preseed);
        }

        /// <summary>O casper lê o preseed linha a linha; um '\r' colado no fim do valor entra
        /// no hash da senha, que aí simplesmente não autentica.</summary>
        [Fact]
        public void BuildPreseed_UsesUnixLineEndings() =>
            Assert.DoesNotContain("\r", UbiquityPreseedBuilder.BuildPreseed(Config(), Hash, "d=/dev/sda", isReplaceMode: false));

        [Fact]
        public void BuildDiskResolutionCommand_Gpt_ResolvesBySeedPartuuidThenParentDisk()
        {
            string command = UbiquityPreseedBuilder.BuildDiskResolutionCommand(Layout(isGpt: true), 3);

            Assert.Contains("PARTUUID=aaaabbbb-cccc-dddd-eeee-ffff00001111", command);
            Assert.Contains("casper-preseed /root partman-auto/disk", command);
        }

        /// <summary>O comando roda no initramfs, onde `lsblk` e `debconf-set` NAO existem —
        /// conferido no initrd.lz real da ISO. Usa-los foi o que impediu o disco alvo de chegar
        /// ao partman em 2026-08-05, e o partman entao escolheu um sozinho.</summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void BuildDiskResolutionCommand_UsesOnlyToolsPresentInTheInitramfs(bool isGpt)
        {
            string command = UbiquityPreseedBuilder.BuildDiskResolutionCommand(Layout(isGpt), 3);

            Assert.DoesNotContain("lsblk", command);
            Assert.DoesNotContain("debconf-set ", command);
        }

        /// <summary>Um `partman-auto/disk` vazio conta como pergunta ja respondida, e e pior
        /// que ausente: o partman segue sem alvo em vez de perguntar.</summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void BuildDiskResolutionCommand_OnlySetsTheTargetWhenItResolved(bool isGpt)
        {
            string command = UbiquityPreseedBuilder.BuildDiskResolutionCommand(Layout(isGpt), 3);

            Assert.Contains("[ -b \"$d\" ]", command);
        }

        /// <summary>A receita chega no initramfs, que some no switch_root — sem a copia para o
        /// filesystem live ela nao existe mais quando o partman vai le-la.</summary>
        [Fact]
        public void BuildDiskResolutionCommand_StagesTheRecipeIntoTheLiveFilesystem() =>
            Assert.Contains(
                "cp /" + UbiquityPreseedBuilder.RecipeFileName + " /root/" + UbiquityPreseedBuilder.RecipeFileName,
                UbiquityPreseedBuilder.BuildDiskResolutionCommand(Layout(isGpt: true), 3));

        /// <summary>Em MBR a assinatura pertence ao disco inteiro, então o blkid já aponta pra
        /// ele — procurar a partição pai ali não faria sentido.</summary>
        [Fact]
        public void BuildDiskResolutionCommand_Mbr_ResolvesBySignatureDirectly()
        {
            string command = UbiquityPreseedBuilder.BuildDiskResolutionCommand(Layout(isGpt: false), 3);

            Assert.Contains("PTUUID=1a2b3c4d", command);
            Assert.DoesNotContain("pkname", command);
        }

        /// <summary>Sem a semente no layout não há identificador de disco — seguir daqui
        /// escolheria um disco por adivinhação, e o modo substituir apaga o que escolher.</summary>
        [Fact]
        public void BuildDiskResolutionCommand_SeedMissingFromLayout_Throws() =>
            Assert.Throws<InvalidOperationException>(
                () => UbiquityPreseedBuilder.BuildDiskResolutionCommand(Layout(isGpt: true), 99));

        /// <summary>No dual-boot as partições existentes são do usuário: a receita só pode
        /// ocupar o espaço livre que o shrink liberou.</summary>
        [Fact]
        public void BuildDualBootRecipe_DoesNotDeclareAnEfiPartition()
        {
            string recipe = UbiquityPreseedBuilder.BuildDualBootRecipe(swapEnabled: false, swapSizeGb: 0);

            Assert.DoesNotContain("method{ efi }", recipe);
            Assert.Contains("mountpoint{ / }", recipe);
        }

        /// <summary>No substituir o disco é reparticionado do zero, então a ESP precisa nascer
        /// junto — no dual-boot ela já existe e é a do Windows.</summary>
        [Fact]
        public void BuildReplaceRecipe_Uefi_CreatesTheEfiPartition() =>
            Assert.Contains("method{ efi }",
                UbiquityPreseedBuilder.BuildReplaceRecipe(isUefi: true, swapEnabled: false, swapSizeGb: 0));

        [Fact]
        public void BuildReplaceRecipe_Bios_HasNoEfiPartition() =>
            Assert.DoesNotContain("method{ efi }",
                UbiquityPreseedBuilder.BuildReplaceRecipe(isUefi: false, swapEnabled: false, swapSizeGb: 0));

        [Theory]
        [InlineData(true, 4, true)]
        [InlineData(false, 4, false)]
        [InlineData(true, 0, false)]
        public void BuildDualBootRecipe_DeclaresSwapOnlyWhenRequested(
            bool enabled, int sizeGb, bool expectSwap)
        {
            string recipe = UbiquityPreseedBuilder.BuildDualBootRecipe(enabled, sizeGb);

            Assert.Equal(expectSwap, recipe.Contains("linux-swap"));
        }
    }
}
