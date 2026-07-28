## Context

O `storage:` do autoinstall é escrito no Windows, antes do reboot, mas só é
consumido pelo curtin depois que o Linux já bootou. O `match:` do subiquity é
declarativo — compara atributos de **disco** (`size`, `serial`, `model`,
`path`...) contra valores já conhecidos no momento em que o YAML foi escrito.
Ele não tem uma chave "disco que contém a partição com PARTUUID X", porque
responder isso exige ler a tabela de partições, e o matcher declarativo não
lê nada, só compara.

Já foram descartados como identificador:
- **Nome de dispositivo** (`Disco 0` vs `/dev/nvme0n1`) — convenções
  diferentes e ordem de enumeração instável entre boots.
- **Número de série** — em NVMe, `MSFT_Disk.SerialNumber` (Windows) e
  `serial` via udev/lsblk (Linux) leem campos diferentes do mesmo
  dispositivo (EUI-64 do namespace vs. serial do controlador); nunca
  coincidem. Já quebrou uma instalação real.
- **Correspondência de campos por tipo de barramento** (ex.: usar o `wwid`
  Linux, que teoricamente corresponde ao EUI-64 que o Windows lê) — descartado
  porque esses campos passam por driver/firmware específico do fabricante.
  Não há garantia de que batem em toda máquina, e a LinuxHub precisa suportar
  hardware arbitrário (NVMe/SATA/USB). Um mapper nesses moldes exigiria
  validação por fabricante, o que não escala.

O que sobra como confiável em qualquer barramento é dado de **tabela de
partição**: escrito por um lado (Windows) e lido pelo outro (Linux, com o
parser dele mesmo) sem tradução de driver no meio. Isso vale para os dois
esquemas de partição que o LinuxHub suporta como alvos de primeira classe
(constitution §6 — GPT e MBR precisam de paridade de segurança, nunca um
"principal" e um "tolerado"):

- **GPT** — o GUID (`PARTUUID`) da partição `CIDATA` que o LinuxHub já cria
  em espaço não alocado do disco alvo, nos dois modos de instalação
  (`CloudInitSeedWriter.CreateSeedPartition`).
- **MBR** — a assinatura de disco de 4 bytes (offset 0x1B8 da MBR), uma
  propriedade do disco inteiro, não de uma partição específica. O Windows
  atribui esse valor quando o disco é inicializado com estilo MBR (o que já
  precisa ter acontecido antes do `New-Partition` da semente `CIDATA`
  funcionar) e o expõe via `Get-Disk.Signature`; o Linux lê o mesmo valor via
  `blkid` (campo `PTUUID` também para tabela `dos`, não só `gpt`) sem
  tradução de driver — é dado de tabela de partição MBR, igual em espírito ao
  PARTUUID do GPT.

## Goals / Non-Goals

**Goals:**
- Identificar o disco alvo do lado Linux por um dado de tabela de partição
  — PARTUUID (GPT) ou assinatura de disco (MBR) — não por ranking de
  tamanho, com paridade entre os dois esquemas (constitution §6).
- Cobrir os casos hoje recusados por `BuildDiskMatch` (disco do meio,
  empate de tamanho) tanto em GPT quanto em MBR, já que nenhuma das duas
  identificações depende de ranking.
- Manter `size: largest`/`smallest` só como último recurso, para o caso
  residual em que nem PARTUUID nem assinatura de disco estão disponíveis
  (ex.: assinatura MBR zerada — ver Risks).

**Non-Goals:**
- Alterar a proteção existente do modo dual-boot (declaração exata de
  partições) — essa camada continua como está; a identificação por
  PARTUUID/assinatura é adicional, não substituta.
- Resolver o caso de dois discos idênticos sem CIDATA (não deveria existir,
  já que o CIDATA é sempre criado no disco escolhido antes do match ser
  gerado).

## Decisions

### D1 — Identificar por PARTUUID da partição CIDATA, resolvido via `early-commands`

O Windows lê o GUID GPT (`Get-Partition.Guid`) da partição `CIDATA` logo
após criá-la e grava esse valor no autoinstall. O Linux resolve, em tempo de
execução (depois do boot da instalação, antes do curtin aplicar o storage),
qual disco físico contém essa partição — ex. via
`lsblk -no pkname /dev/disk/by-partuuid/<guid>` — e ajusta o `match:` do
`user-data` para apontar para esse disco antes da probe de storage rodar.

Isso não pode ser declarativo porque, como descrito em Context, o `match:`
do subiquity não lê tabela de partições — só compara valores. Resolver
PARTUUID → disco pai é inerentemente um passo de execução. O mecanismo do
subiquity para isso é `early-commands`: comandos shell que rodam antes da
probe de storage e podem editar o `user-data`/autoinstall no disco antes do
curtin ler.

### D2 — MBR usa a assinatura de disco de 4 bytes, com o mesmo mecanismo de resolução do GPT

Partição MBR não tem PARTUUID — o conceito é específico de GPT — mas o
disco MBR tem um equivalente na mesma categoria: a assinatura de 4 bytes
gravada na própria tabela MBR (offset 0x1B8), lida pelo Windows via
`Get-Disk.Signature` e pelo Linux via `blkid`/`lsblk -o PTUUID` sem
tradução de driver. A mecânica é a mesma do D1: o Windows grava a
assinatura no autoinstall, e o `early-commands` resolve, em tempo de
execução, qual disco físico tem essa assinatura, ajustando o `match:`
antes da probe de storage.

A diferença prática é o que se lê e onde: PARTUUID é atributo de uma
**partição** (a `CIDATA`, criada pelo próprio LinuxHub); assinatura MBR é
atributo do **disco inteiro** e já existe antes da criação da `CIDATA`
(Windows atribui a assinatura ao inicializar o disco com estilo MBR, pré-
requisito para o `New-Partition` da semente funcionar). Isso significa que,
para MBR, a leitura acontece mais cedo no fluxo — não depende de esperar a
criação da partição semente, só do disco já ter estilo MBR definido, que é
sempre o caso quando o LinuxHub já enumerou o disco no wizard.

Só cai em `size: largest`/`smallest` o caso residual em que a assinatura
lida é `0x00000000` — ver Risks.

**Alternativas consideradas (comuns a D1 e D2):**
- Somar tamanho exato em bytes ao match (rejeitada — ainda é ranking,
  não fecha "disco do meio" nem discos idênticos).
- Mapper de campo por tipo de barramento (rejeitada — depende de
  comportamento de fabricante, não escala para hardware arbitrário).

### D3 — `early-commands` edita o `user-data` no lugar, não reescreve o storage inteiro

O ajuste é cirúrgico, igual para GPT e MBR: substituir o valor do `match:`
(de `size: largest` por `path: /dev/<disco-resolvido>`) num único ponto do
arquivo, via `sed` com delimitador `|` (o path substituído contém `/`), antes
do curtin ler. Evita reconstruir o YAML de storage inteiro dentro do script
shell do `early-commands`, o que seria mais frágil e mais difícil de testar.
O mesmo mecanismo de `early-commands` cobre os dois casos — só muda o
comando de resolução (`by-partuuid` vs. leitura de `PTUUID`/assinatura), não
a estrutura do script. O valor usado é sempre o `path` resolvido, nunca um
serial relido por uma segunda ferramenta — ver "Incidente" acima.

## Risks / Trade-offs

- **[Risco] Ordem de execução do `early-commands` relativa à probe de
  storage do subiquity não foi confirmada na documentação oficial** → precisa
  ser validada (ver Open Questions) antes da implementação prosseguir além
  de um spike; se `early-commands` rodar tarde demais, a abordagem inteira
  (GPT e MBR) não funciona e a mudança precisa ser reavaliada.
- **[Risco] Estabilidade do path resolvido (`/dev/nvme0n1` vs.
  `/dev/disk/by-id/...`)** → preferir `by-id`/`by-partuuid` sempre que
  possível em vez de `/dev/nvmeXnY`, que pode reordenar entre boots do
  próprio ambiente live.
- **[Risco] Editar `user-data` via `sed` em `early-commands` é frágil a
  mudanças de formatação do YAML gerado** → mitigado com um marcador
  inequívoco no `match:` gerado (`DiskPathPlaceholder`) que o `sed` procura,
  coberto por teste que gera o YAML e confirma que o padrão do `sed` bate.
  Delimitador do `sed` é `|`, não `/` — o valor substituído (`$disk`) é um
  path e conteria o delimitador padrão, quebrando o comando (achado no
  mesmo teste manual do incidente acima, corrigido junto).
- **[Risco] Assinatura MBR pode ser `0x00000000`** — discos MBR nunca antes
  inicializados por um Windows específico podem ter assinatura zerada (valor
  usado historicamente pelo Windows como "não atribuída"), e colisões de
  assinatura entre discos clonados por imagem (`dd`, restauração de backup)
  são um problema documentado do próprio Windows → se a assinatura lida for
  `0x00000000`, ou se mais de um disco reportar a mesma assinatura, o sistema
  recusa a identificação por assinatura e cai no fallback de `size:`
  (mesma regra de recusa seletiva que já existe hoje para tamanho ambíguo),
  nunca assume um match arriscado.
- **[Trade-off] Complexidade aceita conscientemente**: a alternativa
  puramente declarativa (mapper de campo) foi descartada por não ser
  confiável em hardware arbitrário; o custo de ter um passo de execução no
  boot da instalação é o preço dessa confiabilidade, não uma escolha de
  conveniência — e agora se paga duas vezes (GPT e MBR) para manter a
  paridade exigida pela constitution §6.
- **[Risco] CIDATA precisa existir e ter GUID capturado antes do
  `AutoinstallStorageBuilder` rodar (caso GPT)** → se a ordem de chamadas
  hoje já grava o `user-data` antes de criar a partição semente, a sequência
  de chamadas em `InstallWizardViewModel`/`AutoinstallPreparationService`
  precisa mudar; detalhar em `tasks.md`. Não se aplica à assinatura MBR, que
  já existe antes da criação da `CIDATA`.

## Open Questions — resolvidas (spike de documentação, 2026-07-28)

- **`early-commands` roda antes da probe de storage?** Confirmado pela
  documentação oficial: "A list of shell commands to invoke as soon as the
  installer starts, in particular before probing for block and network
  devices." O `/autoinstall.yaml` é explicitamente **relido do disco depois**
  de `early-commands` rodar, exatamente para permitir essa reescrita — não é
  um workaround não suportado, é o mecanismo documentado para este caso.
  ([Autoinstall reference — Subiquity](https://canonical-subiquity.readthedocs-hosted.com/en/latest/reference/autoinstall-reference.html))
- **`blkid --match-tag PARTUUID` resolve o disco pai em `early-commands`?**
  Sim — é o padrão documentado/usado na comunidade para descobrir discos por
  PARTUUID antes do particionamento. `blkid` reporta `PTUUID` tanto para
  tabela `gpt` quanto `dos` (MBR), então o mesmo comando cobre os dois casos
  do design (D1 e D2), só troca a tag consultada (`PARTUUID` vs `PTUUID`).
- **`match:` final deve ser `path:` ou `serial:`?** Decisão original (spike de
  documentação): gerar `serial:`, por parecer mais estável entre boots.
  **Essa decisão estava errada e foi corrigida após um teste real** — ver
  "Incidente" abaixo. `match:` **não** suporta nenhuma chave derivada de
  tabela de partição diretamente (não existe `wwn` nem `partuuid` como chave
  de disco) — confirma que a resolução via `early-commands` continua sendo
  necessária, não uma alternativa evitável.

Sem risco bloqueante restante para a seção 1 de `tasks.md`; implementação
liberada para a seção 2 em diante.

## Incidente — `serial:` corrigido para `path:` (teste manual, 2026-07-28)

Na primeira instalação real de teste, o `early-commands` rodou e resolveu o
disco corretamente — o log mostra o `sed` substituindo o marcador por um
valor de serial plausível (`KP352LAKBKDH`) — e mesmo assim o curtin morreu
com `matched no disk` em `Filesystem/apply_autoinstall_config`, o mesmo
sintoma do incidente original que motivou esta mudança inteira.

Causa: `lsblk -dno serial` (usado no `early-commands` para ler o serial do
disco resolvido) e o probe interno do subiquity nem sempre derivam o
`serial` do mesmo jeito — é a mesma classe de divergência entre ferramentas
que já existia entre Windows e Linux, só que desta vez entre duas
ferramentas do próprio Linux. Ler o serial era, na prática, reintroduzir uma
segunda leitura sujeita a divergência exatamente no último passo, depois de
já ter resolvido o disco certo com toda a confiabilidade do PARTUUID/
assinatura.

Correção: `early-commands` para de ler o serial. Ele substitui o marcador
pelo **path do disco já resolvido** (`$disk`, ex. `/dev/sda`) — o mesmo
valor, sem reinterpretação por outra ferramenta, usado no `match: path:`.
Como a resolução e o uso acontecem dentro do mesmo boot (o `early-commands`
resolve o path e o curtin lê o `match:` poucos segundos depois, na mesma
sessão), a preocupação original de "path é menos estável entre boots" (que
motivou preferir `serial:`) não se aplica aqui: não há reboot entre a
resolução e o consumo do valor.

Também corrigido nesse mesmo commit: o `sed` usava `/` como delimitador, que
quebraria ao substituir um valor contendo `/` (todo path de disco contém);
trocado para `|`.
