using LinuxHub.Common.Models;
using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// <paramref name="IsoGrubPath"/> é o caminho da ISO dentro do volume que a hospeda, no
    /// formato do GRUB (<c>/ISOs/arch.iso</c>) — nunca um caminho do Windows, porque o GRUB
    /// não conhece letra de unidade.
    ///
    /// <paramref name="Unattended"/> chega já resolvido pelo preparer do mecanismo em uso: o
    /// construtor da entrada não sabe (nem deve saber) que subiquity usa <c>autoinstall</c> ou
    /// que o archiso usa <c>script=</c>.
    /// </summary>
    /// <param name="IsoHostPartitionUuid">PARTUUID da partição onde a ISO está, quando o
    /// construtor declara precisar dele (<see cref="IIsoBootEntryBuilder.RequiresIsoHostPartitionUuid"/>).
    /// Vazio para os que não precisam.</param>
    public sealed record IsoBootEntryRequest(
        string DistroName,
        string IsoGrubPath,
        UnattendedBootParameters Unattended,
        string IsoHostPartitionUuid = "");

    /// <summary>
    /// Monta a <c>menuentry</c> do GRUB que inicia a sessão live de uma ISO a partir de um
    /// arquivo no disco. Uma implementação por <see cref="LiveSessionFamily"/>: as receitas
    /// não têm denominador comum útil — no casper o GRUB monta o laço e passa
    /// <c>iso-scan/filename</c>; no archiso quem monta o laço é o initramfs, a partir de
    /// <c>img_dev</c>/<c>img_loop</c>, e o GRUB só diz onde a ISO está.
    ///
    /// Existe como abstração, e não como condicional dentro de um gerador só, porque o
    /// caminho do casper é o único que hoje funciona de ponta a ponta: uma família nova não
    /// pode entrar editando o código que ele usa (design.md, decisão 12).
    /// </summary>
    public interface IIsoBootEntryBuilder
    {
        /// <summary>A família de sessão live que esta implementação sabe bootar.</summary>
        LiveSessionFamily Family { get; }

        /// <summary>
        /// Se esta receita precisa que o app nomeie a partição onde a ISO está. O casper não
        /// precisa — ele varre os discos procurando o arquivo indicado em
        /// <c>iso-scan/filename</c>. O archiso precisa, porque quem monta o laço é o initramfs
        /// dele, e o parâmetro <c>img_dev</c> espera um dispositivo nomeado.
        ///
        /// É uma capacidade declarada pelo construtor, e não um <c>if</c> de família em quem
        /// chama: quem paga o custo de descobrir esse identificador é só quem precisa dele.
        /// </summary>
        bool RequiresIsoHostPartitionUuid { get; }

        string Build(IsoBootEntryRequest request);
    }
}
