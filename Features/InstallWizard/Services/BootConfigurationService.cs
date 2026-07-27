using System.Text.RegularExpressions;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Antes desta implementação, <c>AddBootEntry</c> criava uma entrada BCD do tipo
    /// <c>bootsector</c> sem device/path — inutilizável (ver tasks.md 3.6). Uma revisão
    /// intermediária criava a entrada via <c>/create /application osloader</c>, mas isso
    /// falhava em teste real com <c>0xc000007b</c> ("formato de imagem inválido") — o tipo
    /// <c>osloader</c> exige que o binário siga o contrato de loader NT (tipo
    /// <c>winload.efi</c>), que o GRUB não segue. A forma correta de registrar um aplicativo
    /// EFI de terceiros (GRUB, rEFInd, systemd-boot) via BCD é copiar a entrada existente do
    /// <c>{bootmgr}</c> (que já sabe encadear pra outro <c>.efi</c> arbitrário) e só trocar
    /// device/path/descrição — não criar do zero como <c>osloader</c>.
    /// </summary>
    public sealed partial class BootConfigurationService : IBootConfigurationService
    {
        /// <summary>Sufixo que <see cref="BootStagingService"/> põe na descrição de toda
        /// entrada de staging, e por onde <see cref="RemoveStaleStagingEntriesCommands"/> as
        /// reconhece pra apagar.</summary>
        internal const string StagingEntryMarker = "LinuxHub staging";

        /// <summary>
        /// Apaga entradas de staging de execuções anteriores antes de criar a nova. Sem isso
        /// cada tentativa deixava um <c>bcdedit /copy</c> órfão pra trás — inclusive as
        /// quebradas das versões que registravam no <c>{bootmgr}</c>, que continuam
        /// aparecendo (e falhando) no menu de boot do Windows pra sempre.
        ///
        /// Agrupa a saída de <c>bcdedit /enum all</c> em blocos separados por linha em branco
        /// em vez de casar regex na saída inteira: os RÓTULOS do bcdedit são traduzidos pro
        /// idioma do Windows, mas os nomes de datatype (<c>identifier</c>, <c>description</c>)
        /// não são — então casar por eles funciona em qualquer locale. O regex de GUID não
        /// pega ids conhecidos como <c>{bootmgr}</c>/<c>{fwbootmgr}</c> porque as letras deles
        /// não são todas hexadecimais. Falhas aqui são ignoradas de propósito (sem checar
        /// <c>$LASTEXITCODE</c>): não conseguir limpar lixo antigo não é motivo pra abortar a
        /// instalação.
        /// </summary>
        private const string RemoveStaleStagingEntriesCommands = @"
$blocks = @()
$current = @()
foreach ($line in (bcdedit /enum all)) {
    if ($line.Trim() -eq '') {
        if ($current.Count -gt 0) { $blocks += ,@($current); $current = @() }
    } else {
        $current += $line
    }
}
if ($current.Count -gt 0) { $blocks += ,@($current) }
foreach ($block in $blocks) {
    $text = $block -join ""`n""
    if ($text -notmatch '" + StagingEntryMarker + @"') { continue }
    $found = [regex]::Match($text, 'identifier\s+(\{[0-9a-fA-F-]+\})')
    if (-not $found.Success) { continue }
    $stale = $found.Groups[1].Value
    bcdedit /set '{fwbootmgr}' displayorder $stale /remove
    bcdedit /displayorder $stale /remove
    bcdedit /delete $stale /f
}
";

        public string AddFirmwareBootEntry(string description, char driveLetter, string efiPathOnVolume)
        {
            string script = $"$ErrorActionPreference = 'Stop'\n{BuildAddFirmwareBootEntryCommands(description, driveLetter, efiPathOnVolume)}";
            string output = ElevatedPowerShellRunner.Run(script, "registro da entrada de boot BCD");

            return ExtractGuidOrThrow(output);
        }

        /// <summary>
        /// Comandos bcdedit puros (sem <c>$ErrorActionPreference</c> nem elevação própria) —
        /// pensado pra ser embutido dentro de um script maior que já monta a letra de
        /// unidade temporária da ESP (ver <see cref="BootStagingService"/>). A letra
        /// referenciada em <paramref name="driveLetter"/> precisa continuar montada até
        /// esses comandos terminarem — chamar isso DEPOIS de desmontar a ESP falha com
        /// "dispositivo não é válido como especificado" (bcdedit não resolve uma letra que
        /// não existe mais). Define <c>bootsequence</c> (boot único) além de
        /// <c>displayorder</c> — a próxima reinicialização entra direto na entrada do
        /// LinuxHub, sem o usuário precisar escolher nada num menu de boot; da reinicialização
        /// seguinte em diante volta sozinho a bootar o Windows por padrão (mesma proteção que
        /// o "boot único" do Windows usa pra firmware setup/recovery).
        ///
        /// IMPORTANTE: a entrada precisa entrar na lista de boot da FIRMWARE/UEFI
        /// (<c>{fwbootmgr}</c>), não na do Windows Boot Manager (<c>{bootmgr}</c>). Os
        /// subcomandos <c>bcdedit /displayorder</c> e <c>bcdedit /bootsequence</c> NÃO aceitam
        /// objeto-alvo — a própria ajuda do bcdedit diz que eles agem sobre "o gerenciador de
        /// inicialização", ou seja, sempre <c>{bootmgr}</c>. Passar <c>{fwbootmgr}</c> como
        /// primeiro argumento não redireciona o alvo: ele vira só mais um id DENTRO da lista.
        /// O jeito certo de mexer no <c>{fwbootmgr}</c> é <c>bcdedit /set {fwbootmgr} …</c>,
        /// que aceita o id do objeto a modificar. Com a forma errada, o GRUB acabava na lista
        /// do Windows Boot Manager, que tenta carregá-lo como um loader NT e falha com
        /// <c>0xc000007b</c> (tela de erro do "Gerenciador de Inicialização do Windows"
        /// apontando pro grubx64.efi — bug encontrado em teste real, duas vezes).
        ///
        /// <c>displayorder</c> deixa a entrada permanente no menu da firmware; <c>bootsequence</c>
        /// no <c>{fwbootmgr}</c> é o <c>BootNext</c> da UEFI (boot único).
        /// </summary>
        internal static string BuildAddFirmwareBootEntryCommands(string description, char driveLetter, string efiPathOnVolume) =>
            RemoveStaleStagingEntriesCommands + $@"
$create = bcdedit /copy '{{bootmgr}}' /d ""{description}""
if ($LASTEXITCODE -ne 0) {{ throw ""bcdedit /copy falhou: $create"" }}
$match = [regex]::Match($create, '\{{[0-9a-fA-F-]+\}}')
if (-not $match.Success) {{ throw ""Não foi possível extrair o GUID da entrada BCD criada: $create"" }}
$guid = $match.Value
bcdedit /set $guid device partition={driveLetter}:
if ($LASTEXITCODE -ne 0) {{ throw ""bcdedit /set device falhou"" }}
bcdedit /set $guid path '{efiPathOnVolume}'
if ($LASTEXITCODE -ne 0) {{ throw ""bcdedit /set path falhou"" }}
bcdedit /set '{{fwbootmgr}}' displayorder $guid /addlast
if ($LASTEXITCODE -ne 0) {{ throw ""bcdedit /set {{fwbootmgr}} displayorder falhou"" }}
bcdedit /set '{{fwbootmgr}}' bootsequence $guid
if ($LASTEXITCODE -ne 0) {{ throw ""bcdedit /set {{fwbootmgr}} bootsequence falhou"" }}
Write-Output ""BCDGUID:$guid""
Write-Output '--- estado final: bcdedit /enum {{fwbootmgr}} ---'
bcdedit /enum '{{fwbootmgr}}'
Write-Output '--- estado final: entrada criada ---'
bcdedit /enum $guid";

        internal static string BuildAddFirmwareBootEntryScript(string description, char driveLetter, string efiPathOnVolume) =>
            $"$ErrorActionPreference = 'Stop'\n{BuildAddFirmwareBootEntryCommands(description, driveLetter, efiPathOnVolume)}";

        /// <summary>Extrai o GUID BCD da saída de <see cref="ElevatedPowerShellRunner.Run"/>
        /// depois de rodar <see cref="BuildAddFirmwareBootEntryCommands"/> — usado tanto pela
        /// execução standalone (<see cref="AddFirmwareBootEntry"/>) quanto pela embutida em
        /// <see cref="BootStagingService"/>. Casa a linha <c>BCDGUID:</c> especificamente, e não
        /// o primeiro <c>{...}</c> qualquer da saída: os comandos de limpeza que rodam antes
        /// (<see cref="RemoveStaleStagingEntriesCommands"/>) também imprimem saída do bcdedit,
        /// que pode conter GUIDs de entradas antigas.</summary>
        internal static string ExtractGuidOrThrow(string scriptOutput)
        {
            var match = BcdGuidRegex().Match(scriptOutput);
            if (!match.Success)
            {
                throw new InvalidOperationException(
                    $"A entrada BCD pode não ter sido criada corretamente — GUID não encontrado na saída: {scriptOutput}");
            }

            return match.Groups[1].Value;
        }

        [GeneratedRegex(@"BCDGUID:(\{[0-9a-fA-F-]+\})")]
        private static partial Regex BcdGuidRegex();
    }
}
