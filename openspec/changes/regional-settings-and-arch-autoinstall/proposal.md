## Why

**Duas dores independentes, uma origem comum: o app decide sozinho coisas que
deveriam ser do usuário.**

A primeira já afeta quem usa o app hoje. `SystemInfoProvider` devolve o layout de
teclado chumbado em `"us"` e o fuso chumbado em `"America/Sao_Paulo"`. Só o
idioma é lido do Windows. O resultado é um sistema instalado em português com
teclado americano — sem `ç`, acentuação fora do lugar — para quem tem ABNT2, e
fuso de São Paulo para quem mora em qualquer outro lugar do mundo. O encanamento
para levar esses valores até o instalador já existe inteiro; o que está errado é
a origem, e não existe nenhuma tela onde corrigir.

A segunda é uma capacidade que falta. Hoje só o Ubuntu instala sozinho. A
tentativa com o Mint foi abandonada por um motivo estrutural: no `partman`, a
chave que **liga** o modo automático é a mesma que arma o disco inteiro, então
não existia falha segura possível. O `archinstall` não tem esse defeito — ele
nomeia a partição alvo explicitamente e oferece `--dry-run`, que valida a
configuração inteira sem tocar em um setor. São exatamente as duas propriedades
cuja ausência custou a ESP do usuário em 2026-08-05.

E o Arch traz algo que nenhuma outra distro do catálogo permite: escolher o
ambiente gráfico na instalação, porque é a única que chega sem nenhum.

## What Changes

**Configurações regionais (afeta Ubuntu e Arch)**

- Idioma, layout de teclado e fuso horário passam a ser **detectados do Windows**
  e **revisáveis pelo usuário** num passo do wizard, em vez de chumbados no
  código.
- O fuso passa a ser convertido pelo `TimeZoneInfo.TryConvertWindowsIdToIanaId()`
  da BCL, que já resolve o mapeamento Windows→IANA sem tabela manual.
- O layout de teclado passa a ser derivado do layout ativo do Windows, com uma
  tabela de mapeamento para os keymaps que o Linux espera.
- Nenhuma mudança no transporte: `AutoinstallBuilder`, `UbiquityPreseedBuilder` e
  `InstallerConfigWriter` já carregam esses três campos.

**Boot do Arch a partir do Windows**

- A montagem da entrada de boot deixa de ser um bloco único de casper e passa a
  ser escolhida pelo **dado declarado na distro**, com uma implementação por
  família de sessão live. A entrada do Arch segue a receita do próprio archiso
  (`img_dev=` / `img_loop=`), que é outra coisa que a do casper.
- O padrão do dado é casper: toda entrada existente do catálogo continua
  produzindo a mesma entrada de boot de hoje. É pré-requisito da instalação
  desatendida — sem boot não há sessão live onde instalar.

**Instalação desatendida do Arch**

- Novo mecanismo `Archinstall` no enum `UnattendedInstallMechanism`, com gerador
  e preparer próprios, resolvidos pelo registry que já existe.
- O app **não gera o JSON do `archinstall`**: gera um script que o gera na
  sessão live. O levantamento mostrou que a configuração endereça partição por
  caminho de kernel (`/dev/nvme0n1p3`), que não existe do lado do Windows —
  então o app nomeia o alvo por PARTUUID e a tradução acontece no único lugar
  onde ela é possível. Tudo que não depende da máquina continua sendo dado
  literal emitido pelo app.
- Particionamento por alvo declarado: `"wipe": false` no dispositivo e
  `status: "existing"` na ESP do Windows. PARTUUID que não resolve interrompe o
  script antes de chamar o instalador.
- Entrega pelo parâmetro de boot `script=`, com o arquivo gravado ao lado da ISO
  — o mesmo lugar onde o app já grava o cpio do Ubiquity.
- GRUB como bootloader do sistema instalado, com `removable` desligado
  explicitamente (o default do `archinstall` instalaria no caminho de mídia
  removível, que numa máquina com Windows é o fallback do firmware).
- Seleção de ambiente gráfico (Hyprland, GNOME, KDE Plasma e outros) via
  `profile_config`, exposta na UI **apenas** para distros cujo mecanismo suporta.
- Correção do link do Arch no catálogo, que hoje aponta para uma build já
  removida do mirror e retorna 404.

**Sem breaking changes.** O caminho do Ubuntu continua funcionando igual; os três
campos regionais passam a ter valores corretos em vez de chumbados.

## Capabilities

### New Capabilities
- `unattended-install`: declarar e executar instalação desatendida por mecanismo,
  com a exigência de que o alvo do particionamento seja nomeado explicitamente e
  de que exista um modo de validação sem efeito destrutivo antes de qualquer
  execução real.

### Modified Capabilities
- `install-wizard`: as informações regionais deixam de ser valores fixos e
  passam a ser detectadas e revisáveis; surge a seleção de ambiente gráfico para
  os mecanismos que a suportam.
- `distro-catalog`: passa a exigir que o link direto aponte para uma build ainda
  disponível no mirror — restrição nova, criada pelo Arch, cujas ISOs são
  removidas em poucos meses.

## Impact

**Código novo**
- `Features/InstallWizard/Services/` — gerador do script de instalação do Arch,
  o preparer correspondente (ao lado de `SubiquityInstallPreparer` e
  `UbiquityInstallPreparer`), a abstração de entrada de boot com as duas
  implementações (casper e archiso), e o mapeamento de layout de teclado do
  Windows.
- `Features/InstallWizard/ViewModels` + `Views` — passo de configuração regional
  e seletor de ambiente gráfico.

**Código alterado**
- `SystemInfoProvider` — deixa de devolver constantes.
- `InstallerConfig` — campo opcional para o ambiente gráfico.
- `UnattendedInstallMechanism` — novo valor.
- `DistroInfo` — dado novo declarando a família de sessão live da build.
- `GrubConfigBuilder` — passa a delegar a entrada da ISO em vez de montá-la.
  A entrada gerada para as distros existentes não muda.
- `DistroCatalog` — entrada do Arch: versão e link direto.
- `Common/Localization/Strings*.resx` — rótulos do novo passo, nos dois idiomas.

**Externo / operacional**
- O mirror do Arch mantém apenas ~3 releases mensais. O catálogo passa a ter uma
  entrada que **expira**, ao contrário de Ubuntu e Mint, cujas ISOs ficam anos no
  ar. Isso precisa entrar no processo de release.
- O `archinstall` muda o schema do JSON entre versões, o que amarra a
  configuração gerada à build declarada no catálogo.

**Fora de escopo (decidido)**
- Nenhum desktop pré-configurado ou dotfiles. O Arch é instalado limpo, com o
  ambiente gráfico escolhido pelo usuário.
- As demais chaves do subiquity que hoje não usamos (`drivers`, `codecs`,
  `updates`, `packages`, `ssh`).
- systemd-boot e partição XBOOTLDR.
- Limpeza da partição de staging no modo substituir do Arch. O transporte exige
  mantê-la montada durante toda a instalação, então ela sobra no disco ao final.
  Pendência declarada, com dono a definir — não é risco anotado e esquecido.

**Testes**
- `tests/LinuxHub.Tests/` — geração do JSON, mapeamento de teclado e conversão de
  fuso, todos sem rede e sem UI.
