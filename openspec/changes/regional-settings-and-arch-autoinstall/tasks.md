## 1. Detecção regional (lógica pura, sem UI)

- [x] 1.1 Implementar a conversão de fuso do Windows para IANA com `TimeZoneInfo.TryConvertWindowsIdToIanaId()` (BCL, .NET 6+). Usar a forma `Try`: fuso sem correspondência devolve o caso em vez de lançar, e cai num padrão declarado.
- [x] 1.2 Implementar a leitura do layout de teclado ativo do Windows e o mapeamento para o keymap do Linux, numa classe pura (sem `System.Windows`, §5), cobrindo os layouts comuns — incluindo ABNT2 (`br`), que é o caso que originou este change.
- [x] 1.3 Garantir que "sem correspondência" devolve um padrão **declarado e sinalizável**, nunca um valor arbitrário silencioso. É a diferença entre o bug atual e o comportamento novo.
- [x] 1.4 Substituir as constantes de `SystemInfoProvider.GetKeymap()` e `GetTimezone()` pelas detecções acima. Manter `GetLocale()`, que já lê do Windows.
- [x] 1.5 Testes em `tests/LinuxHub.Tests/` para conversão de fuso e mapeamento de teclado, incluindo o caminho "sem correspondência". Sem rede, sem UI.
- [x] 1.6 Rodar `dotnet test` — suíte inteira verde.

## 2. Passo de configuração regional no wizard

- [x] 2.1 Expor idioma, layout de teclado e fuso na ViewModel do wizard, pré-preenchidos com o detectado e editáveis.
- [x] 2.2 Criar o passo na UI seguindo o padrão das telas existentes, com os campos ligados por binding e sem `Click=` chamando lógica (§1).
- [x] 2.3 Adicionar os rótulos em `Common/Localization/Strings.resx` **e** `Strings.en-US.resx` — chave presente em um só arquivo cai no fallback silencioso do `LocalizationManager`.
- [x] 2.4 Garantir que o valor exibido é o que vai para o `InstallerConfig`: nenhuma divergência entre o que o usuário viu e o que foi gravado.
- [x] 2.5 Testes de ViewModel cobrindo "aceita o detectado" e "corrige o detectado".
- [ ] 2.6 **Validação manual**: instalar o Ubuntu com um layout ABNT2 configurado no Windows e confirmar, no sistema instalado, que o teclado tem `ç` e acentuação corretos e que o fuso bate. É o defeito que originou este change; sem exercitá-lo de ponta a ponta, não está fechado.

## 3. Levantamento na ISO do Arch (antes de qualquer código do mecanismo)

Resultados em `research-archinstall.md`, com a procedência de cada fato.

Constitution §6.1 — nada que rode fora do Windows é assumido presente. Foi pular
isto que custou a ESP do usuário no change anterior.

- [x] 3.1 Baixar a ISO `archlinux-2026.08.01-x86_64.iso` e registrar neste change qual versão do `archinstall` ela traz.
- [x] 3.2 Levantar o schema JSON **dessa versão** — em especial `disk_config`, `profile_config`, `locale_config` e `bootloader`. O schema muda entre releases; a documentação online pode não corresponder.
- [x] 3.3 Confirmar que `--silent` e `--dry-run` existem nessa versão e se comportam como documentado. `--dry-run` é o gate deste change; se não funcionar, o plano muda.
- [x] 3.4 Descobrir como `manual_partitioning` referencia uma partição existente — qual é o formato de `obj_id` (PARTUUID, caminho ou id interno). Conecta com o mecanismo já resolvido em `identify-disk-by-partuuid`.
- [x] 3.5 Confirmar que o profile do ambiente gráfico desejado existe nessa versão e como referenciá-lo, incluindo o greeter.
- [x] 3.6 Confirmar como o config chega à sessão live pelo parâmetro `script=`, e o que ele aceita (caminho local? URL?) — é documentado no `README.bootparams` do `mkinitcpio-archiso`, aceita os dois, e o transporte fechado está na decisão 10 do `design.md`.
- [x] 3.7 Se 3.6 falhar, parar e redesenhar a entrega antes de seguir — acionada: `obj_id` e o transporte derrubaram duas premissas, e os artefatos foram revistos antes de qualquer código do mecanismo.

## 4. Entrada de boot do Arch (pré-requisito de tudo que vem depois)

Sem boot não há sessão live, e sem sessão live nenhuma prova do gate pode ser
produzida. A regra que rege esta seção é não colocar em risco o único caminho de
instalação que hoje funciona: nada de condicional dentro do gerador atual
(decisão 12 do `design.md`).

- [ ] 4.1 **Antes de mexer em qualquer coisa**: teste de caracterização travando a entrada de boot gerada hoje para as distros do catálogo. É ele, e só ele, que prova depois que o Ubuntu não regrediu.
- [ ] 4.2 Extrair a montagem da entrada da ISO para uma abstração, com a implementação casper carregando o texto atual **sem alteração nenhuma**.
- [ ] 4.3 Declarar a família de sessão live como dado em `DistroInfo`, com casper como padrão, e resolver a implementação por esse dado — nunca por `distro.Id` (§2), mesmo padrão do `UnattendedInstallPreparerRegistry`.
- [ ] 4.4 Implementar a entrada do archiso a partir da receita do fornecedor (`configs/releng/grub/loopback.cfg`): kernel e initramfs em `/arch/boot/x86_64/`, `archisobasedir`, `img_dev=PARTUUID=…`, `img_loop=…`. Sem `loopback loop` — quem monta o laço é o initramfs.
- [ ] 4.5 Emitir `copytoram=n` na entrada do Arch, com o porquê no código: com o default, o archiso desmonta `/run/archiso/img_dev` ainda no initramfs e o `script=` some antes do login (decisão 10). É um default que falha calado.
- [ ] 4.6 Declarar a família archiso na entrada do Arch do catálogo. Isto **não** é o gate — o gate é `UnattendedInstall`, que continua `None`.
- [ ] 4.7 Suíte verde, com o teste de 4.1 passando sem ter sido editado.
- [ ] 4.8 **Em VM, com snapshot antes.** Bootar o Arch pela staging nos dois modos até a sessão live. É aqui que se prova o `ntfs3` no initramfs (Open Question do `design.md`) e que `/run/archiso/img_dev` está montado na sessão.
- [ ] 4.9 Só depois de 4.8 passar: `IsTested = true` para o Arch, e ajustar o teste que fixa quais distros estão testadas.

## 5. Gerador do script de instalação do Arch

O app gera um script que gera o JSON na sessão live (decisão 9 do `design.md`).
Tudo que não depende da máquina é dado literal emitido pelo app; só os caminhos
de partição são resolvidos em runtime.

- [ ] 5.1 Adicionar o valor `Archinstall` ao enum `UnattendedInstallMechanism`, sem declarar ainda para nenhuma distro no catálogo.
- [ ] 5.2 Implementar o gerador do script como classe pura, testável sem rede e sem UI (§5), por comparação de texto — como `AutoinstallBuilder` e `GrubConfigBuilder` já são.
- [ ] 5.3 O script resolve os PARTUUIDs da raiz e da ESP com as ferramentas da sessão live e **sai sem chamar o `archinstall`** se algum não resolver. Teste travando essa saída: é o que separa "falha parando" de "instala em outro disco" (§6.1).
- [ ] 5.4 Emitir o particionamento com `"wipe": false` no dispositivo e `status: "existing"` na ESP do Windows, com o `dev_path` resolvido. **Teste travando as duas coisas** — são elas que separam "instalar ao lado" de "apagar o Windows".
- [ ] 5.5 Emitir `bootloader_config` com `"Grub"` **e `"removable": false`**, travado em teste. O default `true` instalaria no caminho de mídia removível, que numa máquina com Windows é o fallback do firmware; e systemd-boot não cabe numa ESP de 100 MB (decisão 4).
- [ ] 5.6 Emitir `locale_config.sys_lang`, `locale_config.kb_layout` e `timezone` a partir do `InstallerConfig` — os mesmos três campos da Parte A, que já saem no formato que o `archinstall` espera.
- [ ] 5.7 Emitir `profile_config` com `main: "Desktop"` e o ambiente escolhido em `details`, junto com o greeter padrão dele — sem greeter a máquina liga num terminal.
- [ ] 5.8 No modo substituir, declarar a partição de staging como `existing` e não apagá-la (decisão 11): é dela que a sessão live está lendo a própria raiz. Teste travando.
- [ ] 5.9 Implementar o preparer do mecanismo ao lado de `SubiquityInstallPreparer` e `UbiquityInstallPreparer`, resolvido pelo `UnattendedInstallPreparerRegistry` existente — nada de `if (distro.Id == "arch")` (§2). Ele grava o script ao lado da ISO e devolve o `script=/run/archiso/img_dev/…` nos parâmetros de boot.
- [ ] 5.10 Suíte verde.

## 6. Escolha de ambiente gráfico na UI

- [ ] 6.1 Adicionar o campo opcional de ambiente gráfico ao `InstallerConfig` (decisão 5 do `design.md`).
- [ ] 6.2 Expor a lista na ViewModel, com visibilidade derivada do **mecanismo declarado** pela distro — mesma regra de `IsAutoinstallToggleVisible`, nunca a identidade da distro.
- [ ] 6.3 Adicionar o seletor à UI. Os nomes próprios (Hyprland, GNOME, KDE Plasma) são dado, isentos de localização por §4; os rótulos ao redor não são e vão nos dois `.resx`.
- [ ] 6.4 Teste travando que o seletor não aparece para distro cujo mecanismo não suporta a escolha.

## 7. Catálogo do Arch

- [x] 7.1 Atualizar a entrada do Arch: `Version` para `2026.08.01` e `DirectDownloadLink` para a build correspondente. O link anterior apontava para `2026.01.01`, **já removida do mirror** — era um 404 em produção.
- [x] 7.2 Registrar no comentário da entrada que ela expira: o mirror mantém apenas ~3 releases mensais, diferente de Ubuntu e Mint. E por que o link genérico `latest` não foi usado (decisão 6 do `design.md`).
- [x] 7.3 **NÃO** declarar `UnattendedInstallMechanism.Archinstall` para o Arch. Esta é a linha do gate — ela só muda depois de 8.2 a 8.6 passarem (§7.1).
- [x] 7.4 Adicionar ao processo de release a revisão periódica da entrada do Arch, antes de a build declarada sair do ar.

## 8. Validação real (o gate)

A ordem importa: o boot (seção 4) é pré-requisito, o dry-run é barato e pega erro
de configuração, e a VM é cara e pega o resto. Inverter desperdiça snapshots.

- [ ] 8.1 `dotnet build` e `dotnet test` — build limpo e suíte verde.
- [ ] 8.2 **`--dry-run` com a configuração que o script gera na própria sessão live**, nos dois modos. Nenhum setor é tocado; erro aqui é corrigido sem custo. Confirma também que os caminhos resolvidos em runtime são aceitos.
- [ ] 8.3 **Em VM, com snapshot antes.** Modo dual-boot: a instalação conclui sozinha, e ao final a ESP e as partições do Windows estão **intactas** e ambos os sistemas bootam. Capturar os logs do instalador.
- [ ] 8.4 **Em VM, com snapshot antes.** Idem no modo substituir, confirmando que a staging preservada não quebrou a sessão live no meio da instalação.
- [ ] 8.5 Confirmar que o ambiente gráfico escolhido inicia sozinho no primeiro boot, sem nenhum comando do usuário — instalar o ambiente sem o meio de iniciá-lo entrega uma máquina que liga num terminal.
- [ ] 8.6 Confirmar que o GRUB instalado coube na ESP existente sem que ela precisasse ser redimensionada, e que foi para `\EFI\arch\` — não para o caminho de mídia removível.
- [ ] 8.7 Só depois de 8.2–8.6 passarem: declarar `Archinstall` para o Arch no catálogo e ajustar os testes que fixam quais distros declaram mecanismo.
- [ ] 8.8 Instalação do Ubuntu para confirmar ausência de regressão no caminho subiquity, agora com os campos regionais vindos da detecção e a entrada de boot vinda da abstração nova.
- [ ] 8.9 Registrar em `TEST_MATRIX.md` o que ficou coberto por boot real e o que permanece coberto só por teste unitário — incluindo a partição de staging que sobra no modo substituir, que é pendência declarada e não risco anotado.
