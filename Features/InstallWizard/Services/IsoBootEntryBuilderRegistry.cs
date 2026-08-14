using System.Collections.Generic;
using System.Linq;
using LinuxHub.Common.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    public interface IIsoBootEntryBuilderRegistry
    {
        IIsoBootEntryBuilder Resolve(LiveSessionFamily family);
    }

    /// <summary>
    /// Resolve o construtor da entrada de boot pela família declarada na distro, num lugar só —
    /// mesmo papel que o <see cref="UnattendedInstallPreparerRegistry"/> cumpre para os
    /// mecanismos de instalação desatendida, e pela mesma razão: sem isto, cada ponto que
    /// precisasse saber "é casper ou archiso?" viraria um <c>if</c> próprio, e uma família nova
    /// obrigaria a caçar todos eles.
    /// </summary>
    public sealed class IsoBootEntryBuilderRegistry : IIsoBootEntryBuilderRegistry
    {
        private readonly IReadOnlyDictionary<LiveSessionFamily, IIsoBootEntryBuilder> _builders;

        public IsoBootEntryBuilderRegistry(IEnumerable<IIsoBootEntryBuilder> builders)
        {
            ArgumentNullException.ThrowIfNull(builders);

            // Dois construtores para a mesma família é erro de composição, não uma escolha a
            // resolver em runtime — ToDictionary já lança nesse caso, e é o que queremos.
            _builders = builders.ToDictionary(builder => builder.Family);
        }

        public IIsoBootEntryBuilder Resolve(LiveSessionFamily family)
        {
            if (!_builders.TryGetValue(family, out var builder))
            {
                // Estourar aqui, do lado do Windows, é o resultado bom: sem isso a distro
                // bootaria com a entrada de outra família e o usuário veria uma tela preta
                // depois do reboot, quando o app já não tem como intervir.
                throw new InvalidOperationException(
                    $"A distro declara a família de sessão live '{family}', mas nenhum " +
                    "construtor de entrada de boot para ela foi registrado.");
            }

            return builder;
        }
    }
}
