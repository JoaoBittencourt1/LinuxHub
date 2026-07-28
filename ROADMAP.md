# LinuxHub — Roadmap e Mapeamento de Arquitetura

> Documento de exploração (`/opsx:explore`), 2026-07-25. Serve de base para a
> proposta formal em `openspec/changes/`. Não é spec — é o raciocínio por
> trás dela.
>
> **Atualizado em 2026-07-27** com o estado real após a implementação de
> `openspec/changes/ubuntu-install-pipeline` (Fases 1–4 de código feitas;
> validação em QEMU/hardware real — Fase 5 — ainda não executada). Ver
> `openspec/changes/ubuntu-install-pipeline/TEST_MATRIX.md` para o que falta
> rodar antes de considerar isso pronto para uso real.

## 1. Estado atual do projeto

```
┌─────────────────────────────────────────────────────────────────────────┐
│ WINDOWS (app C#)                          │  REBOOT  │  LINUX (bash)     │
├─────────────────────────────────────────────────────────────────────────┤
│ Catálogo distros        ████████████ 100% │          │                   │
│ Download/seleção ISO    ████████████ 100% │          │                   │
│ Inventário disco (WMI)  ████████████ 100% │          │                   │
│ Wizard UI + config      ████████████ 100% │          │                   │
│ install.conf → grava    ████████████ 100% │          │                   │
│ Diskpart (shrink, alvo  ████████████ 100% │          │ lib/disk.sh 100%  │
│   real, sem create)     ░░░░░░░░░░░░      │  reboot  │ lib/mount.sh 100% │
│ Boot-staging (bcdedit/  ████████░░░░  70%* │  manual  │ lib/chroot.sh100% │
│   MBR, orquestração)    ░░░░░░░░░░░░      │          │ lib/user.sh  100% │
│                                            │          │ lib/boot.sh  100% │
│                                            │          │ distros/     100% │
│                                            │          │  ubuntu.sh        │
└─────────────────────────────────────────────────────────────────────────┘
```
\* Boot-staging: toda a orquestração C# está pronta (ESP/BCD, backup+escrita
de MBR, geração de `grub.cfg`), mas depende de binários GRUB2 pré-compilados
que **não existem no repo** — ver `Assets/Grub/README.md`. Sem eles, o
staging falha com um erro claro (`FileNotFoundException`), não silenciosamente.

Feito e sólido:
- `Features/Catalog` — catálogo de distros, localizado, orientado a dados.
- `Features/InstallWizard` — download de ISO (progresso + cancelamento),
  seleção manual com validação, inventário de disco/partição via WMI,
  wizard MVVM completo, `InstallerConfig` + `InstallerConfigWriter`,
  confirmação destrutiva explícita, boot-staging conectado ao fluxo real.
- `installer/core/lib/{disk,mount,chroot,user,boot}.sh` e
  `installer/distros/ubuntu.sh` — payload Linux completo (particionamento
  real, revalidação do plano, debootstrap, criação de usuário via
  `chpasswd`, bootloader definitivo com chainload de volta pro Windows).
- Infra de localização, MVVM, OpenSpec/constitution já em uso.

Corrigido nesta mudança (estavam quebrados antes):
- `BootConfigurationService` agora cria uma entrada BCD `osloader` real
  (device+path+displayorder), não mais `bootsector` incompleto.
- `InstallWizardViewModel.Install()` agora chama `IDiskPartitioningService`
  e `IBootStagingService` de verdade.
- `DiskPartitioningService` opera no disco/partição real selecionado no
  wizard, não mais hardcoded em `select volume C`.
- `InstallerConfigBuilder` usa `EspLocatorService` (lookup real por GUID
  GPT) em vez de `EfiPartitionIndex = 1` fixo.
- Detecção de UEFI via `GetFirmwareType` (Win32) em vez de heurística de
  pasta.
- `CryptoHelper.GenerateSha512Hash` gerava um digest, não um hash crypt(3)
  — removido; a senha agora é hasheada no lado Linux via `chpasswd`
  (glibc do sistema instalado), não mais no Windows.

Não feito / bloqueado:
- **Nenhum bloqueio de asset restante** — UEFI (`Assets/Grub/uefi/
  grubx64.efi`) e BIOS legado (`Assets/Grub/bios/{boot,core}.img`, com
  embutimento automatizado do `core.img` no gap pós-MBR) têm todo o código e
  os binários necessários, gerados via WSL em 2026-07-27. Ver
  `Assets/Grub/README.md` para como o formato do patch do `boot.img` foi
  determinado (comparação byte a byte contra um `grub-bios-setup` real).
- **Nada disso foi validado por um boot real** — nem UEFI nem BIOS legado
  rodaram contra QEMU ou hardware físico ainda. A validação mais forte
  disponível até agora é a comparação byte a byte contra a ferramenta GRUB
  real, feita num disco sintético via WSL — não substitui testar de verdade.
- **Nenhuma validação em QEMU nem hardware real** — Fases 3.8/5.8/9.2/9.3
  de `tasks.md` inteiras pendentes; ver `TEST_MATRIX.md`. Todo o código
  acima foi escrito e testado unitariamente (lógica pura: geração de
  scripts, `grub.cfg`, particionamento) mas **nunca executado** contra um
  disco/firmware real.
- `resolve_target_disk_device` (`lib/disk.sh`) assume que a ordem de
  enumeração de disco é a mesma em Windows e Linux — limitação conhecida,
  mitigada por `revalidate_plan` mas não eliminada.

## 2. O buraco maior que o diskpart: como entrar no Linux sem USB

Antes de "como particionar", falta responder: como a máquina sai do Windows
rodando e entra num ambiente Linux capaz de rodar `install.sh`, sem USB?
Isso é o cerne da premissa "instalador universal sem USB" e hoje não existe
nenhum código endereçando isso.

**Decisão tomada** (ver seção 4, ponto 2 do usuário): alocar
dinamicamente um espaço no disco físico (SSD/HD) e usá-lo como se fosse um
pendrive Ventoy — a ISO fica como arquivo em um volume existente (ou numa
área dedicada), e um bootloader (GRUB2) é chainloaded a partir do BCD do
Windows e faz boot do `.iso` via loopback, exatamente como o Ventoy faz a
partir de USB. Isso resolve a pergunta em aberto da exploração anterior:
**diskpart deixa de ser necessário pra essa etapa** — só é usado quando o
modo é dual-boot (para encolher a partição NTFS e liberar espaço). Ver
seção 4.

Implicações:
- Cada família de distro tem seu próprio jeito de bootar a partir de um ISO
  em loopback (cmdline: `iso-scan/filename=` no Ubuntu/Debian/casper,
  `findiso=` no Arch, `inst.stage2=` no Fedora...). Isso é pesquisa por
  distro, não código genérico.
- Reaproveitar os scripts/motor de boot do Ventoy (que já resolveu
  compatibilidade de loopback pra dezenas de distros) é uma forma de
  derriscar essa pesquisa em vez de refazer do zero.

## 3. Diskpart / gerenciamento de disco — reframe

O ponto que motivou essa exploração — "o replace pode não funcionar" — está
certo, e a razão é estrutural: **o Windows não consegue reparticionar ou
formatar o disco de onde ele mesmo está rodando, enquanto está rodando.**
Não é limitação do diskpart, é física do SO.

```
              O QUE O WINDOWS PODE FAZER              O QUE SÓ O LINUX PODE FAZER
              (app roda AGORA, disco em uso)          (após reboot, disco livre)
┌──────────────────────────────────┐        ┌──────────────────────────────────┐
│ Dual-boot:                       │        │ Dual-boot:                       │
│  shrink NTFS → espaço não        │  plano │  criar partição real no espaço   │
│  alocado (seguro em disco ativo, │ ──────▶│  livre, mkfs, mount              │
│  é o que o "Gerenciamento de     │        │                                  │
│  Disco" nativo já faz)           │        │ Replace:                         │
│                                   │        │  wipefs/clean/mklabel + mkfs no  │
│ Replace:                         │        │  disco inteiro — só seguro fazer │
│  NADA no disco de sistema.       │        │  isso de fora do disco que está  │
│                                   │        │  sendo apagado                  │
└──────────────────────────────────┘        └──────────────────────────────────┘
```

Conclusão de arquitetura: `DiskPartitioningService` do lado Windows não
deveria "particionar" de verdade — deveria **só fazer o shrink** (única
operação reversível e segura em disco vivo) e **gravar o plano** em
`install.conf`. A criação real de partição/filesystem — e todo o modo
`replace` — vira trabalho do `lib/disk.sh` do lado Linux, executado só
depois do reboot, quando o disco-alvo não está mais em uso pelo SO
que está rodando. Isso também bate com a constitution §6 (ponto de
não-retorno real): o ponto de não-retorno é o reboot, não o clique em
"Instalar" no Windows.

## 4. Decisões do usuário (2026-07-25)

1. **Foco em Ubuntu primeiro.** Outras distros (família Arch, Fedora, etc.)
   vêm depois, via atualização incremental — cada uma como uma integração
   própria, não um recurso genérico "grátis".
2. **Boot sem USB = área alocada dinamicamente no disco, ao estilo
   Ventoy/pendrive físico.** Diskpart só entra para o caso de dual-boot
   (encolher partição existente). Isso resolve a pergunta em aberto da
   seção 2 a favor de GRUB2 chainload + loopback (opção A/C da exploração
   anterior), não VHD-boot.
3. **Rust/C fica de fora por enquanto.** C# continua no lado Windows (com
   acesso completo a Win32/PowerShell Storage cmdlets quando precisar de
   mais controle que o diskpart-via-subprocesso) e bash continua no lado
   Linux. Revisitar só se surgir uma necessidade concreta que C#/bash não
   resolvam.
4. **Precisa funcionar em UEFI e BIOS legado** — nota: "UEFI" e "EFI" não
   são dois tipos diferentes de firmware (EFI é o antecessor/mesma família
   do UEFI); os dois tipos reais são **UEFI vs BIOS/Legacy**, com
   mecanismos de boot e tabela de partição diferentes entre si (GPT+ESP vs
   MBR/GPT+bios_grub).

## 5. Viabilidade

O lado Windows (catálogo, wizard, geração de config) está maduro — perto de
terminado com polimento focado. A camada de plano de disco (shrink,
detecção de UEFI/ESP correta) é bem escopada e de risco baixo.

O mecanismo de boot sem USB + o payload Linux (particionamento real,
instalação base por distro, bootloader, preservação do boot do Windows) são
de outra categoria: equivalem a construir, do zero, o núcleo de uma
mini-distro instaladora. É onde projetos assim costumam travar — exige
validação em hardware real (firmware UEFI varia entre fabricantes), e cada
distro nova é um subprojeto de integração, não um item de catálogo.

Recomendação seguida: provar o pipeline inteiro ponta a ponta com Ubuntu
antes de generalizar. **Decisão**: a instalação real (particionar, base do
sistema, bootloader) é feita em bash manual — `debootstrap` + `parted`/
`sgdisk` + chroot — em vez de gerar um arquivo `autoinstall`/subiquity. Dá
mais controle e transparência sobre cada passo (importante em operação
destrutiva), ao custo de reimplementar em bash algo que o subiquity já
resolve pronto. `lib/disk.sh`, `lib/mount.sh`, `lib/chroot.sh`,
`lib/boot.sh` e `distros/ubuntu.sh` são, portanto, scripts reais a escrever
— não geradores de config de um instalador nativo.

> **Nota (2026-07-28):** esta decisão específica (bash manual em vez de
> `autoinstall`) foi revertida na prática — `openspec/changes/
> ubuntu-install-pipeline` e `identify-disk-by-partuuid` implementaram e
> validaram (com correção pós-teste real) a geração de `autoinstall`/
> subiquity para o Ubuntu. Ver seção 6 para o registro atualizado do porquê
> e do que isso implica de escopo.

## 6. Decisões do usuário (2026-07-28) — escopo da instalação automática por distro

Depois de validar o pipeline de `autoinstall` ponta a ponta com Ubuntu
(`identify-disk-by-partuuid`), surgiu a pergunta natural: generalizar esse
mesmo mecanismo para toda distro do catálogo?

**Decisão: não.** A instalação totalmente automática (autoinstall/preseed
gerado pelo LinuxHub, sem intervenção do usuário) fica restrita a um
conjunto pequeno e deliberado de distros — **Ubuntu e Linux Mint
confirmados, mais algumas outras a avaliar caso a caso** (candidatas
naturais: derivadas Ubuntu/Debian, que compartilham subiquity/preseed ou
mecanismo equivalente). Para todo o resto do catálogo, o LinuxHub entrega
a máquina até a tela do instalador nativo da distro (boot sem USB,
particionamento/plano de dual-boot já preparado quando aplicável) e para
aí — **o usuário conduz a instalação a partir dali**, usando o instalador
que a própria distro já tem.

Por quê:
- **Custo de suporte não escala linearmente.** Cada distro teria seu
  próprio formato de instalação desatendida (subiquity no Ubuntu,
  Calamares em várias, `archinstall`/scripts no Arch, Anaconda no
  Fedora/RHEL, `d-i`/preseed no Debian puro...) — não é "mais um item de
  catálogo", é uma integração nova por família, com sua própria superfície
  de bugs (o incidente do `serial:` vs `path:` em `identify-disk-by-partuuid`
  é o tipo de problema que se paga de novo a cada mecanismo novo).
- **Automatizar destrói o motivo de escolher certas distros.** Arch (e
  primos no mesmo espírito) existem porque a instalação manual É o
  produto — customização de partição, kernel, init, pacote base. Gerar um
  `archinstall` genérico por trás das costas do usuário remove exatamente
  o que a distro promete entregar. Não é uma economia de esforço, é
  entregar a coisa errada.
- Isso não é regressão do "instalador universal sem USB" — essa promessa
  (seção 2) continua valendo para qualquer distro do catálogo. O que muda
  é onde a responsabilidade do LinuxHub termina: ele sempre resolve "como
  eu saio do Windows e entro num Linux capaz de instalar", mas só resolve
  também "como esse Linux se instala sozinho" para o conjunto restrito
  acima.

Implicação de arquitetura: o catálogo de distros (`distro-catalog`) precisa
de um jeito de marcar, por distro, se ela suporta o caminho de autoinstall
do LinuxHub ou só o caminho de boot-staging — isso é uma mudança de spec, a
propor formalmente em `openspec/changes/` antes de implementar (constitution
§7), não uma flag improvisada no meio do wizard.
