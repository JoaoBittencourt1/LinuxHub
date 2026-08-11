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
    public sealed record IsoBootEntryRequest(
        string DistroName,
        string IsoGrubPath,
        UnattendedBootParameters Unattended);

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

        string Build(IsoBootEntryRequest request);
    }
}
