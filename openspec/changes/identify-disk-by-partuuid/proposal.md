## Why

O disco alvo é escolhido no Windows, antes do reboot, mas quem executa a
instalação é o Linux, depois. Hoje o único elo entre os dois é
`match: size: largest`/`smallest` — uma posição de ranking, não uma
identidade. Se o conjunto de discos mudar entre o planejamento e o boot (ex.:
plugar um HD externo maior antes de reiniciar), o match continua
sintaticamente válido e aponta para o disco errado sem avisar. No modo
substituir isso apaga um disco inteiro sem checagem nenhuma. Nome de
dispositivo e número de série já foram descartados (ver
`PROBLEMA-IDENTIFICACAO-DO-DISCO.md`); esta proposta resolve o problema
identificando o disco por dado de tabela de partição — GUID da partição
`CIDATA` em GPT, assinatura de disco em MBR —, ambos já gravados/gravável
pelo LinuxHub antes do reboot, com paridade entre os dois esquemas
(constitution §6: nenhuma proteção de disco pode cobrir só GPT ou só MBR).

## What Changes

- O Windows passa a ler o GUID GPT (`PARTUUID`) da partição `CIDATA` logo
  após criá-la, e a assinatura de disco MBR (`Get-Disk.Signature`) quando o
  disco alvo é MBR, gravando o valor correspondente no autoinstall em vez de
  descartá-lo.
- O `storage:` gerado passa a incluir um bloco `early-commands` que resolve,
  em tempo de execução no Linux já bootado, qual disco físico corresponde ao
  identificador conhecido (PARTUUID ou assinatura), e ajusta o `match:` do
  autoinstall para apontar exatamente para esse disco antes do curtin rodar
  a probe de storage. Isso vale para os dois modos de instalação (dual-boot
  e substituir) e os dois esquemas de partição (GPT e MBR). **BREAKING**: o
  `match: size: largest/smallest` deixa de ser o critério primário; passa a
  ser usado só como último recurso, quando nem PARTUUID nem assinatura estão
  disponíveis de forma segura (ex.: assinatura MBR zerada ou duplicada).
- `AutoinstallStorageBuilder.BuildDiskMatch` deixa de lançar exceção para
  "disco do meio"/empate: a identificação por PARTUUID/assinatura não
  depende de ranking, então esses casos passam a funcionar em vez de serem
  recusados — em GPT e em MBR igualmente.
- Modo dual-boot mantém sua rede de proteção existente (partições declaradas
  com offset/size exatos) e passa a ganhar a mesma identificação por
  PARTUUID/assinatura como camada adicional, não substituta.

## Capabilities

### New Capabilities
- `disk-target-identification`: como o disco físico escolhido no Windows é
  identificado com segurança do lado Linux durante a instalação automatizada
  — geração/gravação do identificador de referência (PARTUUID em GPT,
  assinatura de disco em MBR), resolução em tempo de execução via
  `early-commands`, e a regra de último recurso quando nenhum dos dois está
  disponível.

### Modified Capabilities
(nenhuma — a capability `disk-provisioning` proposta em
`ubuntu-install-pipeline` ainda não foi sincronizada para
`openspec/specs/`, então não há spec principal existente para alterar via
delta neste momento; a integração entre as duas mudanças é tratada no
`design.md`.)

## Impact

Implementado. Desvio em relação ao plano original: o GUID da partição
`CIDATA` e a assinatura MBR do disco **não** passam por
`CloudInitSeedWriter` — os dois já eram alcançáveis pela mesma leitura WMI
que `DiskLayoutProvider` já fazia para as demais partições/disco, então
foram lidos ali (uma fonte a menos de I/O de disco para o mesmo dado, DRY).

- `Features/InstallWizard/Models/DiskLayout.cs` — `PartitionLayout` ganhou
  `Guid`; `DiskLayout` ganhou `DiskSignatureHex` e `HasUniqueDiskSignature`.
- `Features/InstallWizard/Services/DiskLayoutProvider.cs` — lê `Guid` de
  `MSFT_Partition` e `Signature` de `MSFT_Disk`, e computa unicidade da
  assinatura entre os discos MBR da máquina.
- `Features/InstallWizard/Services/EarlyCommandsBuilder.cs` (novo) — gera o
  script de `early-commands` que resolve PARTUUID/assinatura para o disco
  físico via `blkid`, sem I/O.
- `Features/InstallWizard/Services/AutoinstallStorageBuilder.cs` —
  `BuildDiskMatch`/`BuildWholeDiskLayout`/`BuildDualBootConfig` passaram a
  receber `seedPartitionNumber`; novo `BuildEarlyCommands` expõe o script
  quando aplicável.
- `Features/InstallWizard/Services/AutoinstallBuilder.cs` — `BuildUserData`
  passou a receber `seedPartitionNumber` e insere `early-commands:` no nível
  raiz do YAML quando não-nulo.
- `Features/InstallWizard/Services/AutoinstallPreparationService.cs` — passa
  o `seedPartitionNumber` já conhecido adiante.
- Testes: `AutoinstallStorageBuilderTests`, `AutoinstallBuilderTests` e o
  novo `EarlyCommandsBuilderTests` cobrem os dois caminhos (GPT-com-PARTUUID
  e MBR-com-assinatura) e o fallback de último recurso. Suíte completa
  (133 testes) passando; `CloudInitSeedWriterTests` não precisou de
  alteração (ver desvio acima).
- Pendente: validação manual em VM (seção 7 de `tasks.md`) — não executável
  neste ambiente.
