## 1. Provisionamento da partição de staging

- [x] 1.1 Criar `IStagingPartitionService` + `StagingPartitionService` em
      `Features/InstallWizard/Services/`, seguindo o padrão elevado do
      `CloudInitSeedWriter` (script PowerShell via `ElevatedPowerShellRunner`,
      marcador de sucesso, extração do número da partição).
- [x] 1.2 Implementar cálculo do tamanho necessário: tamanho da ISO + folga fixa,
      exposto como propriedade para a pré-checagem poder consultá-lo sem criar nada.
- [x] 1.3 Implementar a criação: `New-Partition` sem letra de unidade, `Format-Volume`
      NTFS com rótulo próprio (ex.: `LHSTAGING`), devolvendo número e PARTUUID.
- [x] 1.4 Extrair a lógica de abrir espaço hoje embutida em
      `CloudInitSeedWriter.BuildCreateScript` para um ponto único que calcula o total
      (staging + semente) e executa **um** shrink (design D4).
- [x] 1.5 Escrever testes de montagem de script para 1.3 e 1.4, incluindo verificação
      de que não há continuação de linha por crase (armadilha já encontrada:
      crase não é escape em string verbatim do C#).

## 2. Cópia da ISO e verificação

- [x] 2.1 Implementar a cópia da ISO para a partição de staging com relatório de
      progresso via `IProgress<T>`, montando a partição temporariamente e
      desmontando ao final.
- [x] 2.2 Verificar integridade comparando o tamanho do arquivo copiado com o
      original; abortar com erro explícito em divergência (spec `iso-staging-media`).
- [x] 2.3 Adicionar chave de progresso em `Strings.resx` e `Strings.en-US.resx` para
      a etapa de cópia.
- [x] 2.4 Testes: cópia truncada aborta; sucesso só é reportado com tamanhos iguais.

## 3. Pré-checagem de espaço

- [x] 3.1 Estender a validação de `InstallWizardViewModel.BeginInstall` para recusar
      quando não houver espaço para staging + semente, antes de qualquer escrita.
- [x] 3.2 Mensagem quantificando necessário vs. disponível, em ambos os `.resx`.
- [x] 3.3 Teste: espaço insuficiente não chega a criar `PendingConfirmation`.

## 4. Boot apontando para a staging

- [x] 4.1 Alterar `GrubConfigBuilder`/`BootStagingService` para gerar o caminho da ISO
      relativo à partição de staging em vez do volume do Windows.
- [x] 4.2 Ajustar `InstallWizardViewModel.RunInstall` para ordenar: shrink único →
      criar staging → copiar ISO → criar semente → boot-staging.
- [x] 4.3 Atualizar os testes de `GrubConfigBuilderTests` afetados pelo novo caminho.
- [ ] 4.4 **Validar em VM**: dual-boot com BitLocker ligado no C: deve bootar a ISO
      normalmente (spec `boot-staging`, cenário do volume criptografado).

## 5. Storage config do modo substituir

- [x] 5.1 Substituir `AutoinstallStorageBuilder.BuildWholeDiskLayout` por uma variante
      da lista explícita: disco `preserve: true`, ESP `preserve: true` + format fat32,
      staging e semente `preserve: true`, partições do Windows omitidas, raiz criada
      no espaço liberado (design D2/D3).
- [x] 5.2 Garantir que `BuildDualBootConfig` também declare a staging como preservada.
- [x] 5.3 Testes em `AutoinstallStorageBuilderTests`: no modo substituir o YAML não
      contém `layout:`; staging e semente aparecem com `preserve: true`; nenhuma
      partição do Windows aparece na lista.
- [ ] 5.4 **Validar em VM**: modo substituir conclui sem
      `clear-holders: FAIL` (o erro que originou esta mudança).

## 6. Recuperação do espaço após a instalação

- [x] 6.1 Gerar, via `late-commands`, um unit systemd `oneshot` que no primeiro boot
      do sistema instalado remove staging e semente por **PARTUUID** e se desabilita.
- [x] 6.2 Blindar o unit: conferir rótulo e filesystem antes de apagar e abortar em
      qualquer divergência — nunca apagar partição que não bata com os dois critérios
      (design D5, risco identificado como o mais perigoso da mudança).
- [x] 6.3 Testes da geração do unit: PARTUUID correto embutido, verificações presentes.
- [ ] 6.4 **Validar em VM**: após o primeiro boot, staging e semente sumiram e nenhuma
      outra partição foi tocada.

## 7. Guardas de segurança

- [x] 7.1 Manter as duas guardas (Secure Boot e BitLocker) no caminho de bloqueio do
      `InstallWizardViewModel` (design D6, revisado).
- [x] 7.2 Ajustar a checagem de BitLocker para olhar o volume que será **encolhido**
      para abrir espaço da staging, não mais o que hospeda a ISO — é ele que passa a
      importar depois que a ISO muda de lugar.
- [x] 7.3 Atualizar a mensagem de BitLocker nos dois `.resx` para explicar o motivo
      atual (encolhimento de volume cifrado + risco de pedido de chave de
      recuperação), já que o motivo original — GRUB não ler a ISO — deixou de valer.

## 8. Fechamento

- [x] 8.1 Rodar a suíte completa e confirmar que nada regrediu.
- [x] 8.2 Atualizar `Assets/Grub/README.md` / `TEST_MATRIX.md` com o que passou a ser
      coberto por teste real em VM e o que continua não validado.
- [x] 8.3 Revisar as Open Questions do `design.md` e registrar as decisões tomadas
      durante a implementação.
