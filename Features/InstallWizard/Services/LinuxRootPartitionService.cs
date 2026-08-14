using System.Text.RegularExpressions;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>Identidade da partição raiz criada para o instalador próprio.</summary>
    public sealed record LinuxRootPartition(
        int DiskIndex,
        int PartitionNumber,
        string PartitionUuid,
        long OffsetBytes,
        long SizeBytes);

    public interface ILinuxRootPartitionService
    {
        LinuxRootPartition Create(int diskIndex);
    }

    /// <summary>
    /// own-linux-installer: cria a partição raiz do Linux no espaço que o encolhimento
    /// liberou, ANTES do reboot, e devolve a identidade dela para o plano.
    ///
    /// Existe porque o instalador próprio não delega particionamento a ninguém (é a razão de
    /// ser da mudança). No caminho do instalador nativo quem cria essa partição é o curtin,
    /// depois do reboot; aqui não há esse alguém, e sem esta partição o instalador live não
    /// tem onde escrever — teria de escolher um alvo sozinho, que é exatamente o que a
    /// memória do projeto ("ler, nunca deduzir o disco") proíbe.
    ///
    /// NÃO formata: a partição sai crua e quem faz <c>mkfs.ext4</c> é o instalador live, que
    /// já assere o disco livre antes de qualquer escrita destrutiva (D11). Formatar aqui
    /// duplicaria a decisão de filesystem em dois lugares.
    /// </summary>
    public sealed class LinuxRootPartitionService : ILinuxRootPartitionService
    {
        /// <summary>Tipo GPT "Linux filesystem". Sem isto o Windows cria a partição como
        /// "basic data" (EBD0A0A2-…), que é o tipo de um volume do Windows: o sistema
        /// instalado apareceria com tipo errado na tabela, e o Explorador ofereceria formatá-la
        /// no primeiro boot de volta ao Windows.</summary>
        internal const string LinuxFilesystemGptType = "{0fc63daf-8483-4772-8e79-3d69d8477de4}";

        private const string SuccessMarker = "LINUXROOT_OK:";

        private readonly IInstallationPlanMutationGuard _mutationGuard;

        public LinuxRootPartitionService(IInstallationPlanMutationGuard mutationGuard)
        {
            _mutationGuard = mutationGuard ?? throw new ArgumentNullException(nameof(mutationGuard));
        }

        public LinuxRootPartition Create(int diskIndex)
        {
            _mutationGuard.EnsurePublishedForDisk(diskIndex);

            string output = ElevatedPowerShellRunner.Run(
                BuildCreateScript(diskIndex),
                $"criação da partição raiz do Linux no disco {diskIndex}");

            return ParseCreateOutputOrThrow(diskIndex, output);
        }

        /// <summary>
        /// <c>-UseMaximumSize</c> ocupa o maior vão livre, que é o que o encolhimento acabou de
        /// abrir. Pedir um tamanho exato em vez disso deixaria uma sobra inutilizável no fim do
        /// disco (o encolhimento reserva folga de alinhamento e a folga da partição semente,
        /// que este caminho não cria).
        /// </summary>
        internal static string BuildCreateScript(int diskIndex) => $@"
$ErrorActionPreference = 'Stop'

$estilo = (Get-Disk -Number {diskIndex}).PartitionStyle
if ($estilo -ne 'GPT') {{
    throw ""O instalador próprio exige disco GPT (o disco {diskIndex} usa $estilo). Em BIOS legado/MBR, use o dual-boot manual ou o modo substituir.""
}}

$partition = New-Partition -DiskNumber {diskIndex} -UseMaximumSize -GptType '{LinuxFilesystemGptType}'
if (-not $partition) {{ throw ""O Windows não criou a partição raiz do Linux no disco {diskIndex}."" }}

$guid = $partition.Guid
if (-not $guid) {{ throw ""O Windows não informou o GUID da partição raiz criada no disco {diskIndex}. Sem ele a partição não pode ser identificada com segurança depois do reboot."" }}

# O número devolvido pela criação NÃO é o número final. O Windows numera as
# partições pela posição no disco, então inserir uma partição fisicamente antes
# de outra já existente renumera a outra — e o número que acabou de sair da
# criação pode já estar apontando para a partição errada.
#
# Bug real, com o alvo errado sendo o pior possível: o plano guardou 6 para a
# raiz, mas a raiz é a partição 5 — a criação devolveu o índice seguinte, não o
# número assentado. A partição 6 é a de RECUPERAÇÃO do Windows. Só a conferência
# de offset do lado live impediu que ela fosse formatada.
#
# O GUID é gravado na tabela GPT e não muda. Reler por ele dá o número que o
# disco realmente tem agora — junto com offset e tamanho já assentados.
$correspondentes = @(Get-Partition -DiskNumber {diskIndex} | Where-Object {{ $_.Guid -eq $guid }})
if ($correspondentes.Count -ne 1) {{
    throw ""Esperava exatamente 1 partição com o GUID $guid no disco {diskIndex}, encontrei $($correspondentes.Count). Sem uma correspondência única a partição não pode ser identificada com segurança.""
}}
$criada = $correspondentes[0]

Write-Output ""{SuccessMarker} $($criada.PartitionNumber) $guid $($criada.Offset) $($criada.Size)""";

        internal static LinuxRootPartition ParseCreateOutputOrThrow(int diskIndex, string output)
        {
            Match match = Regex.Match(
                output ?? string.Empty,
                $@"{SuccessMarker}\s*(\d+)\s+(\{{[0-9a-fA-F-]+\}}|[0-9a-fA-F-]{{36}})\s+(\d+)\s+(\d+)");

            if (!match.Success)
            {
                throw new InvalidOperationException(
                    "A partição raiz do Linux foi criada, mas o Windows não informou número, " +
                    "identificador, offset e tamanho dela — sem esses dados o instalador live " +
                    "não teria como saber onde escrever, e a instalação é interrompida em vez " +
                    $"de deduzir um alvo. Saída recebida: {output}");
            }

            return new LinuxRootPartition(
                DiskIndex: diskIndex,
                PartitionNumber: int.Parse(match.Groups[1].Value),
                PartitionUuid: match.Groups[2].Value.Trim('{', '}'),
                OffsetBytes: long.Parse(match.Groups[3].Value),
                SizeBytes: long.Parse(match.Groups[4].Value));
        }
    }
}
