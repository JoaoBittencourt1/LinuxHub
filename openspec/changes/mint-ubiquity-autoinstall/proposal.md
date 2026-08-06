## Why

O ROADMAP (§6, decisão de 2026-07-28) coloca o Linux Mint dentro do escopo da
instalação automática, mas o suporte nunca saiu do papel: `DistroCatalog` marca
`SupportsAutoinstall` apenas no Ubuntu, e o toggle de instalação automática nem
aparece na UI quando o Mint é a distro selecionada.

O motivo real só ficou claro agora, abrindo o `casper/filesystem.manifest` da
ISO do Linux Mint 22.3 Cinnamon: **o Mint não usa subiquity**. Ele traz
`ubiquity 24.04.3+mint19` + `ubiquity-frontend-gtk` + `ubiquity-casper` — o
instalador legado, que o próprio Ubuntu abandonou na 23.04 — e não tem
`subiquity`, `curtin` nem `cloud-init` instalados. Todo o pipeline de
autoinstall de hoje (`AutoinstallBuilder`, `AutoinstallStorageBuilder`,
`CloudInitSeedWriter`, partição CIDATA, parâmetro `autoinstall` na cmdline) é
escrito no schema do subiquity/curtin e **não tem nenhum efeito no Ubiquity**:
o parâmetro seria ignorado e a partição semente, irrelevante. Marcar
`SupportsAutoinstall = true` no Mint hoje entregaria um toggle que promete
instalação desatendida e devolve o instalador gráfico interativo.

Ubiquity automatiza por outro mecanismo — **debconf/preseed** — que é
incompatível com o YAML do subiquity em formato, em transporte e no modelo de
particionamento (receita `partman` em vez de `storage:` do curtin).

## What Changes

- Generalizar o conceito hoje implícito de "autoinstall" para **mecanismo de
  instalação desatendida por distro**, com duas implementações concretas:
  `subiquity` (Ubuntu, já existente) e `ubiquity-preseed` (Mint, nova).
- **BREAKING (modelo de dados)**: `DistroInfo.SupportsAutoinstall` (bool) passa
  a ser insuficiente — um booleano não diz *qual* mecanismo a distro usa, e o
  gerador precisa dessa informação para escolher entre YAML e preseed. Vira uma
  declaração explícita de mecanismo, com "nenhum" como estado válido.
- Introduzir a geração de **preseed debconf** para o Ubiquity do Mint, cobrindo
  o mesmo conjunto de dados que o autoinstall do Ubuntu já cobre: conta de
  usuário (com hash de senha), hostname, locale/timezone/keymap, plano de
  particionamento (dual-boot e replace) e reboot ao final sem prompt de mídia.
- Definir e implementar o **transporte do preseed** até o Ubiquity numa ISO
  loop-montada e read-only (não dá para injetar arquivo dentro da ISO) — o
  ponto tecnicamente mais aberto desta mudança, resolvido em `design.md`.
- Estender `GrubConfigBuilder` para emitir os parâmetros de cmdline do
  mecanismo escolhido (`automatic-ubiquity`/`only-ubiquity` + `preseed/*` no
  caso do Mint) em vez do `autoinstall` fixo do subiquity.
- Atualizar a entrada do Mint no catálogo (hoje em 22.2, com link direto para a
  ISO 22.2) para a **22.3 Cinnamon**, que é a build alvo desta validação.
- Fora de escopo: qualquer distro além de Ubuntu e Mint; Calamares,
  Anaconda, archinstall ou d-i puro; e mudar o mecanismo já validado do Ubuntu
  (subiquity permanece exatamente como está).

## Capabilities

### New Capabilities
- `unattended-install`: seleção e geração da configuração de instalação
  desatendida conforme o mecanismo do instalador nativo de cada distro —
  autoinstall/cloud-init para instaladores subiquity, preseed/debconf para
  instaladores Ubiquity —, incluindo o transporte dessa configuração até o
  instalador e os parâmetros de boot que a ativam.

### Modified Capabilities
- `distro-catalog`: o catálogo passa a declarar, por distro, **qual** mecanismo
  de instalação desatendida aquela build usa (ou que não há nenhum validado),
  em vez do booleano atual que só responde "sim/não" e assume subiquity.

## Impact

Código afetado:

- `Common/Models/DistroInfo.cs`, `Common/Data/DistroCatalog.cs` — troca do
  booleano pela declaração de mecanismo; entrada do Mint atualizada para 22.3.
- `Features/InstallWizard/Services/` — `AutoinstallBuilder`,
  `AutoinstallStorageBuilder`, `AutoinstallPreparationService`,
  `CloudInitSeedWriter`: hoje são o caminho único e implícito do subiquity;
  passam a ser uma das implementações atrás da seleção por mecanismo. Novo
  gerador de preseed e novo transporte para o caminho do Ubiquity.
- `Features/InstallWizard/Services/GrubConfigBuilder.cs` — parâmetros de
  cmdline deixam de ser o `autoinstall` fixo.
- `Features/InstallWizard/ViewModels/IsoAcquisitionViewModel.cs`
  (`IsAutoinstallToggleVisible`) e `InstallWizardViewModel` — passam a
  consultar o mecanismo em vez do booleano.

Testes afetados: `DistroCatalogTests.Autoinstall_IsClaimedByUbuntuOnly` quebra
por construção ao Mint entrar (é o sinal de que a mudança é deliberada);
`AutoinstallBuilderTests`, `AutoinstallStorageBuilderTests`,
`CloudInitSeedWriterTests` e `GrubConfigBuilderTests` precisam continuar verdes
provando que o caminho do Ubuntu não regrediu.

Dependência externa: a validação depende de um teste real de ponta a ponta com
a ISO do Linux Mint 22.3 Cinnamon — o comportamento de preseed do Ubiquity
empacotado pelo Mint (`+mint19`) não pode ser deduzido da documentação do
Ubuntu, tem que ser confirmado contra a ISO e num boot real.
