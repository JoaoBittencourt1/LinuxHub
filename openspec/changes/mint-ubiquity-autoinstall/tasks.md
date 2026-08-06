## 1. Levantamento na ISO real (antes de qualquer código)

- [x] 1.1 Extrair o pacote `ubiquity` de `pool/` da ISO do Mint 22.3 Cinnamon e
      listar as chaves de preseed que ele de fato honra para conta de usuário
      (`passwd/*`), hostname (`netcfg/get_hostname`) e locale/teclado —
      registrar o resultado neste change, não na documentação do Ubuntu
- [x] 1.2 Levantar no mesmo pacote as chaves de particionamento (`partman*`)
      suportadas e quais receitas cobrem os modos dual-boot e substituir
      (achado que mudou o design: `partman-auto/expert_recipe_file`)
- [x] 1.3 Confirmar como o Ubiquity encerra a instalação sem pedir remoção de
      mídia — `ubiquity/reboot`, distinto do `noprompt` do casper
- [x] 1.4 Confirmar a diferença prática entre `automatic-ubiquity` e
      `only-ubiquity` — não são equivalentes; só o primeiro passa `--automatic`

## 2. Validar o transporte antes de investir no conteúdo

- [x] 2.1 Provar o formato do cpio SVR4 antes de investir no gerador — feito
      com um script descartável, cuja saída depois se mostrou byte a byte
      idêntica à do `CpioArchiveWriter` em C#. O script foi removido: duas
      implementações do mesmo formato só divergem
- [ ] 2.2 Bootar o Mint pelo staging com `initrd (loop)/casper/initrd.lz
      <cpio>` e confirmar, na sessão live, que `casper-set-selections` aplicou
      a chave — prova o mecanismo do design D1 de ponta a ponta
- [ ] 2.3 Se 2.2 falhar, reavaliar D1 antes de seguir (o restante das tarefas
      depende desse transporte)

## 3. Mecanismo declarado no catálogo

- [x] 3.1 Criar o enum `UnattendedInstallMechanism { None, Subiquity,
      UbiquityPreseed }` em `Common/Models`
- [x] 3.2 Substituir `DistroInfo.SupportsAutoinstall` pelo novo campo, com
      `None` como padrão, mantendo o racional do comentário atual
- [x] 3.3 Ajustar `DistroCatalog`: Ubuntu passa a declarar `Subiquity`; demais
      distros ficam em `None`
- [x] 3.4 Atualizar os consumidores do booleano
      (`IsoAcquisitionViewModel.IsAutoinstallToggleVisible`,
      `DistroDetectionService`, `InstallWizardViewModel`)
- [x] 3.5 Reescrever `DistroCatalogTests.Autoinstall_IsClaimedByUbuntuOnly` em
      termos do enum e rodar a suíte — nenhum comportamento observável muda
      neste passo (193 verdes)

## 4. Despacho por mecanismo (sem mudar saída)

- [x] 4.1 Extrair `IUnattendedInstallPreparer` a partir do fluxo atual de
      `AutoinstallPreparationService`
- [x] 4.2 Tornar `AutoinstallPreparationService` a implementação `Subiquity`
      dessa interface, sem alterar o conteúdo que ela gera
- [x] 4.3 Resolver a implementação a partir do mecanismo da distro no ponto que
      hoje chama a preparação diretamente
- [x] 4.4 Mover a decisão dos parâmetros de cmdline para fora de
      `GrubConfigBuilder`, que passa a recebê-los prontos (junto do initrd
      adicional, quando houver), mantendo-o como texto puro
- [x] 4.5 Rodar a suíte completa — `AutoinstallBuilderTests`,
      `AutoinstallStorageBuilderTests`, `CloudInitSeedWriterTests` e
      `GrubConfigBuilderTests` verdes e sem mudança de saída para o Ubuntu

## 5. Gerador de preseed do Ubiquity

- [x] 5.1 Implementar o escritor de cpio SVR4 (cabeçalho `070701`, alinhamento
      de 4 bytes, registro `TRAILER!!!`) com teste que percorre o arquivo
      gerado e recupera o conteúdo
- [x] 5.2 Implementar `UbiquityPreseedBuilder` para conta de usuário, hostname,
      locale, timezone e teclado, usando as chaves levantadas em 1.1 — senha
      sempre em hash, nunca em texto claro
- [x] 5.3 Implementar a receita `partman` do modo dual-boot (usa o espaço livre
      liberado, não apaga partição existente), com as chaves de 1.2
- [x] 5.4 Implementar a receita `partman` do modo substituir, apontando para o
      disco identificado
- [x] 5.5 Resolver o identificador do disco alvo (PARTUUID/assinatura, D5) em
      `preseed/early_command`, reaproveitando o mecanismo de
      `identify-disk-by-partuuid`
- [x] 5.6 Acrescentar o encerramento sem prompt de mídia conforme 1.3
- [x] 5.7 Implementar o preparer `UbiquityPreseed` que monta o preseed, empacota
      no cpio, grava fora da ISO e devolve os parâmetros de boot

## 5b. Correção pós-incidente de 2026-08-05

O primeiro boot real apagou a ESP do usuário (ver `design.md`). Estas tarefas
corrigem as três causas e reabrem o que a correção invalidou.

- [x] 5b.1 Emitir `partman-auto/method` **apenas no modo substituir** — no
      dual-boot ele significa disco inteiro E arma a eleição automática de disco
      quando o alvo está vazio (a causa do incidente). Teste trava as duas coisas
- [x] 5b.2 Trocar `lsblk`/`debconf-set` por `sed` e `casper-preseed` no
      `early_command`, e só gravar o alvo sob `[ -b "$d" ]`
- [x] 5b.3 Copiar a receita do initramfs para o filesystem live no
      `early_command`, e apontar `expert_recipe_file` para lá
- [x] 5b.4 Conferir no `initrd.lz` real que toda ferramenta usada existe
      (`blkid`, `sed`, `cp`, `echo`, `casper-preseed`) e que a expressão de
      derivação do disco acerta NVMe, SATA e eMMC
- [x] 5b.5 Reverter o Mint para `None` no catálogo até haver boot real
- [ ] 5b.6 Depois do teste em VM: descobrir qual pergunta do
      `automatically_partition` precisa de qual resposta sob
      `automatic-ubiquity` — não é um literal (`biggest_free`), é um
      identificador `$dev//$id` calculado em runtime
- [x] 5b.7 Manter as confirmações destrutivas ligadas para o teste em VM
      exercitar o caminho completo — a trava contra máquina real é o catálogo
      em `None` (constitution §7.1), não o instalador parar no meio

## 6. Integração e validação real

- [ ] 6.1 Declarar `UbiquityPreseed` para o Mint no catálogo — SÓ depois de
      6.3/6.4 passarem. A versão 22.3 e o link direto já estão atualizados; a
      declaração foi revertida para `None` após o incidente (constitution §7.1)
- [ ] 6.2 Verificar que o toggle aparece para o Mint e que a entrada de boot
      traz os parâmetros do Ubiquity e nenhum `autoinstall` — depende de 6.1
- [ ] 6.3 **Em VM, com snapshot antes.** Mint 22.3 dual-boot: instala sozinho,
      ou para SÓ na tela de escolha do particionamento (falha segura aceitável,
      fecha 5b.6). A ESP e as partições do Windows precisam sair intactas nos
      dois casos. Capturar `/var/log/installer/` e `/var/log/syslog`
- [ ] 6.4 **Em VM, com snapshot antes.** Idem no modo substituir
- [ ] 6.4a Confirmar nos logs que o `early_command` rodou inteiro: a receita
      chegou em `/linuxhub.recipe` do live e `partman-auto/disk` saiu com o
      device certo
- [ ] 6.5 Instalação real do Ubuntu para confirmar ausência de regressão no
      caminho subiquity
- [x] 6.6 Registrar no `TEST_MATRIX.md` o que foi coberto por boot real e o que
      permanece só coberto por teste unitário
