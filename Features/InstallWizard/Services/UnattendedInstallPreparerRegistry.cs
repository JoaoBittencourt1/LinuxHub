using System.Collections.Generic;
using System.Linq;
using LinuxHub.Common.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Resolve o preparer do mecanismo declarado pela distro. Existe para que a escolha
    /// aconteça num lugar só: sem isto, cada ponto que precisa saber "é subiquity ou
    /// Ubiquity?" viraria um <c>if</c> próprio, e um mecanismo novo obrigaria a caçar todos
    /// eles (design.md, D4).
    /// </summary>
    public sealed class UnattendedInstallPreparerRegistry : IUnattendedInstallPreparerRegistry
    {
        private readonly IReadOnlyDictionary<UnattendedInstallMechanism, IUnattendedInstallPreparer> _preparers;

        public UnattendedInstallPreparerRegistry(IEnumerable<IUnattendedInstallPreparer> preparers)
        {
            ArgumentNullException.ThrowIfNull(preparers);

            // Dois preparers para o mesmo mecanismo é erro de composição, não uma escolha a
            // resolver em runtime — ToDictionary já lança nesse caso, e é o que queremos.
            _preparers = preparers.ToDictionary(preparer => preparer.Mechanism);
        }

        public IUnattendedInstallPreparer Resolve(UnattendedInstallMechanism mechanism)
        {
            if (mechanism == UnattendedInstallMechanism.None)
            {
                throw new InvalidOperationException(
                    "Nenhum mecanismo de instalação desatendida foi declarado para esta distro — " +
                    "este caminho só deve ser percorrido quando o wizard ofereceu (e o usuário " +
                    "aceitou) a instalação automática.");
            }

            if (!_preparers.TryGetValue(mechanism, out var preparer))
            {
                throw new InvalidOperationException(
                    $"A distro declara o mecanismo de instalação desatendida '{mechanism}', mas " +
                    "nenhuma implementação para ele foi registrada.");
            }

            return preparer;
        }
    }
}
