## Why

O modo substituir está quebrado e não é corrigível onde está: o autoinstall usa
`layout: name: direct`, que reescreve o disco inteiro, e o `clear-holders` do curtin
não consegue soltar a partição NTFS que hospeda a ISO — o casper a mantém montada
read-write em `/isodevice` durante toda a sessão live. Confirmado em teste real
(2026-07-29, VM Hyper-V, Ubuntu 24.04.4):

```
ERROR finish: .../cmd-block-meta/clear-holders: FAIL: removing previous storage devices
→ block-meta FAIL → stage-partitioning FAIL → CurtinInstallError, exit 3
```

O dual-boot funciona na **mesma topologia** porque declara a partição hospedeira com
`preserve: true` e o curtin não encosta nela. Ou seja: o problema nunca foi o disco
único, foi o `layout: direct`.

Recusar a instalação e pedir pendrive contraria a premissa do produto ("instalador
universal sem USB"). E o mesmo arranjo — ISO morando numa partição do Windows —
também quebra em máquina com BitLocker, porque o GRUB não lê volume criptografado
(`error: no such device: /Users/.../ubuntu.iso`, erro real na mesma VM).

## What Changes

- **Nova partição de staging**: o LinuxHub cria uma partição NTFS dedicada (~7–8 GB)
  no disco alvo e copia a ISO para ela antes do reboot. NTFS porque o casper só monta
  `ext2/3/4|xfs|jfs|reiserfs|vfat|ntfs|iso9660|btrfs|udf` (lido de
  `scripts/casper-helpers`, `is_supported_fs`) e FAT32 tem teto de 4 GB por arquivo,
  contra 6,2 GB da ISO.
- **O boot de staging passa a apontar para a ISO na partição de staging**, não mais
  para o caminho dela no volume do Windows.
- **BREAKING (comportamento do modo substituir)**: `layout: name: direct` sai. O modo
  substituir passa a emitir lista explícita de `config:` — disco `preserve: true`,
  partição de staging `preserve: true`, partições do Windows **omitidas** (o curtin
  trata o espaço como disponível), raiz criada nesse espaço. O `clear-holders` só
  precisa soltar as partições do Windows, que ninguém segura.
- **Recuperação do espaço da staging** depois da instalação, já no sistema instalado —
  a partição não pode ser apagada enquanto a sessão live depende dela.
- **Pré-checagem de espaço**: sem espaço para a staging, recusar antes de escrever,
  com mensagem explicando quanto falta.

## Capabilities

### New Capabilities
- `iso-staging-media`: provisionar a partição NTFS dedicada, copiar a ISO para ela,
  garantir que ela sobreviva ao particionamento do instalador e recuperar o espaço
  depois que a instalação terminar.

### Modified Capabilities
- `boot-staging`: a ISO deixa de ser lida do volume do Windows e passa a ser lida da
  partição de staging — muda a origem que o `search --file` do GRUB localiza e remove
  a dependência de o volume do Windows ser legível pelo GRUB (BitLocker).
- `linux-install-payload`: o modo substituir deixa de delegar ao `layout: direct` e
  passa a declarar explicitamente o que preservar e o que liberar.

## Impact

**Código afetado:**
- `Features/InstallWizard/Services/AutoinstallStorageBuilder.cs` — `BuildWholeDiskLayout`
  é substituído por uma variante da lista explícita; `BuildDualBootConfig` passa a
  também preservar a staging.
- `Features/InstallWizard/Services/CloudInitSeedWriter.cs` — o mesmo padrão de criação
  de partição elevada já existe aqui (semente de 128 MB) e é o modelo a seguir; a
  abertura de espaço passa a considerar staging + semente juntas.
- `Features/InstallWizard/Services/GrubConfigBuilder.cs` e `BootStagingService.cs` —
  caminho da ISO.
- `Features/InstallWizard/ViewModels/InstallWizardViewModel.cs` — nova etapa no
  `RunInstall`, com progresso (a cópia de ~6 GB não é instantânea).
- `Features/InstallWizard/Services/IsoStorage.cs` — origem da cópia.

**Interação com trabalho não commitado:** `BootSecurityService` (guardas de Secure Boot
e BitLocker) está pronto no working tree. Com a staging, **a guarda de BitLocker deixa
de ser necessária** — a ISO passa a morar numa partição criada pelo LinuxHub, que não é
criptografada, então o GRUB a lê mesmo com o C: cifrado. A guarda de Secure Boot
continua valendo, porque o `grubx64.efi` do projeto não é assinado.

**Desvio arquitetural:** aprofunda o desvio do D1 do `design.md` do
`ubuntu-install-pipeline` ("o Windows só grava intenção") já aberto pelo
`CloudInitSeedWriter` — agora o lado Windows cria uma partição de vários GB e copia
dados para ela. É decisão consciente, registrada aqui, não detalhe de implementação.

**Custo para o usuário:** ~7 GB indisponíveis entre o preparo e a recuperação
pós-instalação, e o tempo de copiar a ISO antes do reboot.
