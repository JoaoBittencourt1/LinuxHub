## 1. Spike de validação (bloqueante)

- [x] 1.1 Confirmar, na documentação oficial do subiquity/curtin ou por
      teste numa VM, que `early-commands` roda antes da probe de storage que
      decide o `match:`. Se não confirmar, registrar o achado em design.md e
      reavaliar a abordagem antes de prosseguir para a seção 2.
      — Confirmado via documentação oficial (ver design.md, Open Questions
      resolvidas): `early-commands` roda antes até da probe de block
      devices, e `/autoinstall.yaml` é relido do disco depois de rodar.
- [x] 1.2 Confirmar o formato de resolução PARTUUID → disco pai disponível
      no ambiente live do instalador Ubuntu (ex.: `/dev/disk/by-partuuid/`
      existe e `lsblk -no pkname` funciona nesse estágio do boot).
      — `blkid --match-tag PARTUUID` é o padrão documentado/usado pela
      comunidade para isso; registrado em design.md.
- [x] 1.3 Confirmar o formato de resolução da assinatura MBR → disco pai no
      mesmo ambiente (ex.: `blkid`/`lsblk -o PTUUID,PKNAME` reporta a
      assinatura de disco `dos` de forma estável nesse estágio do boot).
      — Mesmo mecanismo do `blkid`: reporta `PTUUID` tanto para tabela
      `gpt` quanto `dos`, só troca a tag consultada.
- [x] 1.4 Decidir, com base no spike, se o `match:` pós-resolução usa
      `path:` ou `serial:` lido em tempo de execução (Open Question do
      design.md) — vale para GPT e MBR igualmente.
      — Decisão: `serial:` como critério primário, `path:` como segundo
      item de uma lista ordenada de `match:` (suportado desde subiquity
      24.08.1). Registrado em design.md.

## 2. Captura do identificador no Windows (GPT)

- [x] 2.1 ~~Estender `CloudInitSeedWriter.CreateSeedPartition`~~ — **desvio
      de implementação**: em vez de uma segunda leitura via PowerShell
      (`Get-Partition.Guid`), o GUID sai da mesma query WMI que
      `DiskLayoutProvider` já faz para ler as demais partições
      (`MSFT_Partition.Guid`, adicionada ao `SELECT` existente). Evita uma
      fonte paralela de leitura de disco para o mesmo dado — `CloudInitSeedWriter`
      não precisou mudar.
- [x] 2.2 Propagado via `PartitionLayout.Guid`, presente em toda partição
      lida por `DiskLayoutProvider.GetLayout` (inclui a semente, já que ela
      é criada ANTES dessa leitura). `AutoinstallStorageBuilder` localiza a
      partição semente pelo número (`seedPartitionNumber`, já devolvido por
      `CreateSeedPartition` e passado adiante por `AutoinstallPreparationService`
      → `AutoinstallBuilder.BuildUserData` → `AutoinstallStorageBuilder.Build`).
- [x] 2.3 Ordem já era essa (`Prepare` cria a semente antes de ler o
      layout) — nenhuma mudança necessária.

## 3. Captura do identificador no Windows (MBR)

- [x] 3.1 `DiskLayoutProvider.GetLayout` agora inclui `Signature` no
      `SELECT` de `MSFT_Disk` e expõe como `DiskLayout.DiskSignatureHex`
      (hex minúsculo, formato igual ao `PTUUID` do `blkid`).
- [x] 3.2 Assinatura `0x00000000` vira `string.Empty` em
      `DiskLayoutProvider.FormatSignature` — tratado como "sem
      identificador", não como erro.
- [x] 3.3 Fluxo único para os dois identificadores: `DiskLayout` carrega
      GUID por partição e assinatura por disco simultaneamente;
      `AutoinstallStorageBuilder` decide qual usar conforme `IsGpt`.

## 4. Geração do storage config por identificador

- [x] 4.1 `AutoinstallStorageBuilder.BuildDiskMatch(disk, seedPartitionNumber)`
      resolve por PARTUUID/assinatura antes de cair no ranking; "disco do
      meio"/empate só é recusado quando também não há identificador seguro.
- [x] 4.2 Novo `EarlyCommandsBuilder` (classe própria, sem I/O) gera o
      script; `AutoinstallStorageBuilder.BuildEarlyCommands` expõe o
      resultado já decidido por `ResolveDiskIdentity` (helper privado
      compartilhado com `BuildDiskMatch`, evita decidir a estratégia duas
      vezes de forma divergente).
- [x] 4.3 `AutoinstallBuilder.BuildUserData` chama `BuildEarlyCommands` e
      insere o bloco no nível raiz do YAML quando não-nulo;
      `AutoinstallStorageBuilder` continua sem I/O.
- [x] 4.4 `DiskLayoutProvider.IsUniqueDiskSignature` compara a assinatura
      contra todos os discos MBR da máquina; `HasUniqueDiskSignature: false`
      é o sinal que `AutoinstallStorageBuilder` usa para recusar o
      identificador e cair no critério de tamanho.

## 5. Fallback de último recurso

- [x] 5.1 Coberto por `ResolveDiskIdentity`: sem PARTUUID/assinatura
      utilizável, o comportamento é idêntico ao pré-existente (`size:
      largest`/`smallest`, ou recusa quando nem isso está disponível).

## 6. Testes

- [x] 6.1 `CloudInitSeedWriterTests` não precisou mudar — decisão do
      desvio 2.1 (o Guid não passa mais por lá).
- [x] 6.2 Cobertura via `AutoinstallStorageBuilderTests` (assinatura zerada
      e duplicada) — não há teste dedicado de `DiskLayoutProvider` porque
      ele é I/O de WMI, sem testes unitários no projeto (mesmo padrão de
      `CloudInitSeedWriter`, ver comentário da classe de teste).
- [x] 6.3 `AutoinstallStorageBuilderTests` cobre: disco do meio (GPT e
      MBR), empate de tamanho, assinatura zerada, assinatura duplicada,
      ausência de `early-commands` quando só o tamanho identifica.
- [x] 6.4 `AutoinstallBuilderTests` cobre presença/ausência de
      `early-commands` e a ordem relativa a `storage:`.
- [x] 6.5 `EarlyCommandsBuilderTests` trava o marcador
      (`DiskPathPlaceholder`) usado nos dois lados (geração do `match:` e
      geração do `sed`).

## 7. Validação manual

- [ ] 7.1 Teste real (não-VM) em máquina com disco GPT: **encontrou um bug**
      — `early-commands` resolvia o disco certo, mas `match: serial:`
      morria com "matched no disk" porque `lsblk -dno serial` e o probe
      interno do subiquity derivam o serial de formas diferentes (ver
      "Incidente" em design.md, 2026-07-28). Corrigido: match passou a usar
      `path:` com o valor já resolvido, sem reler via outra ferramenta,
      mais o delimitador do `sed` (`|` em vez de `/`, que quebraria num
      path). Reexecutar o teste completo até a instalação terminar
      confirma se a correção resolveu — ainda não reexecutado.
- [ ] 7.2 Repetir cenário de disco do meio/empate em MBR.
- [ ] 7.3 Testar o cenário do doc original: HD adicional plugado entre o
      planejamento e o reboot — instalação deve continuar aplicando no
      disco correto, não no que passou a ser o maior. Repetir para GPT e
      para MBR.
- [ ] 7.4 Testar disco MBR com assinatura zerada: confirmar que o sistema
      cai no critério de tamanho em vez de tentar usar a assinatura.
