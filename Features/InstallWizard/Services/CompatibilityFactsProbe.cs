using System.Globalization;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Executa o preflight de compatibilidade contra a máquina real e devolve os fatos que
    /// as regras avaliam.
    ///
    /// Existe porque a máquina de regras (<see cref="ICompatibilityPreflightRunner"/>) e o
    /// parser (<see cref="CompatibilityFactsParser"/>) estavam prontos e testados, mas nada
    /// em produção rodava o script: o gate ficava permanentemente aberto e disco dinâmico,
    /// Storage Spaces, RAID/VMD e BitLocker ativo chegavam ao particionamento destrutivo.
    /// Ver constitution §7.1 — o que vem depois do gate fica desarmado se o gate não for
    /// alcançado em runtime.
    /// </summary>
    public interface ICompatibilityFactsProbe
    {
        CompatibilityFacts Read(int diskNumber, char systemDriveLetter);
    }

    /// <inheritdoc />
    public sealed class CompatibilityFactsProbe : ICompatibilityFactsProbe
    {
        public CompatibilityFacts Read(int diskNumber, char systemDriveLetter)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(diskNumber);

            if (!char.IsAsciiLetter(systemDriveLetter))
                throw new ArgumentException("Letra de unidade inválida.", nameof(systemDriveLetter));

            // Argumentos vão como parâmetros nomeados, nunca interpolados no corpo do script
            // (D11). Ambos já foram validados acima, e o próprio script os valida de novo com
            // ValidateRange/ValidatePattern.
            string arguments = string.Format(
                CultureInfo.InvariantCulture,
                "-DiskNumber {0} -SystemDriveLetter {1}",
                diskNumber,
                char.ToUpperInvariant(systemDriveLetter));

            string output = ElevatedPowerShellRunner.RunFile(
                ScriptCatalog.GetPath(ScriptCatalog.CompatibilityPreflight),
                arguments,
                "preflight de compatibilidade");

            return CompatibilityFactsParser.Parse(output);
        }
    }
}
