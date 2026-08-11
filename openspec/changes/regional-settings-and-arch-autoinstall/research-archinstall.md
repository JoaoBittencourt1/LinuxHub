# Levantamento do `archinstall` da ISO 2026.08.01 (tasks 3.1–3.6)

Constitution §6.1 — nada que rode fora do Windows é assumido presente. Este
documento registra o que foi **verificado no artefato**, de onde veio cada fato,
e o que continua **não verificado**.

## Procedência

| Fato | Fonte |
|---|---|
| Versão do `archinstall` na ISO | `iso/2026.08.01/arch/pkglist.x86_64.txt` do mirror — manifesto de pacotes gerado pelo archiso a partir do airootfs desta build |
| Schema JSON, flags, profiles | pacote `archinstall-4.4-1-any.pkg.tar.zst` do Arch Linux Archive — o binário exato que a ISO instala |
| `.automated_script.sh` | pacote `archiso-89-1-any.pkg.tar.zst` (perfil `releng`), versão vigente em 2026-07-26, a que construiu a ISO de 2026-08-01 |

**Não verificado:** o conteúdo do `airootfs.sfs` da ISO em si. Ler o squashfs
exigiria baixar 1,5 GB e um extrator de squashfs, que esta máquina não tem. Onde
isso importa está marcado abaixo.

## 3.1 Versão do `archinstall`

**`archinstall 4.4-1`** (`grub 2:2.14-1`, `mkinitcpio-archiso 73-1`).

## 3.2 Schema JSON da 4.4

Chaves de topo relevantes (`lib/args.py`, `ArchConfig.from_config`):

```jsonc
{
  "locale_config":     { "kb_layout": "br", "sys_lang": "pt_BR.UTF-8", "sys_enc": "UTF-8", "console_font": "default8x16" },
  "bootloader_config": { "bootloader": "Grub", "uki": false, "removable": true },
  "timezone":          "America/Sao_Paulo",   // topo, default "UTC"
  "hostname":          "...",
  "disk_config":       { "config_type": "manual_partitioning", "device_modifications": [ ... ] },
  "profile_config":    { "profile": { "main": "Desktop", "details": ["Hyprland"] }, "greeter": "sddm", "gfx_driver": null }
}
```

- `locale_config.sys_lang` usa exatamente o formato que o `InstallerConfig.Locale`
  já produz (`pt_BR.UTF-8`), e `kb_layout` o do `Keymap` (`br`). Os três campos da
  Parte A entram sem conversão adicional (task 4.5).
- `bootloader` é lido por `Bootloader.from_arg`, que aplica `.capitalize()` antes
  de casar com o enum — o valor canônico é `"Grub"`, e `"GRUB"` também passa.
  `design.md` diz `GRUB`; ambos funcionam nesta versão.
- **`removable` tem default `true`** quando ausente (`BootloaderConfiguration.parse_arg`).
  Com GRUB em UEFI isso instala no caminho de mídia removível
  (`\EFI\BOOT\BOOTX64.EFI`) em vez de `\EFI\arch\` + entrada no firmware. Numa
  máquina com Windows esse caminho é o fallback do próprio firmware. Para
  instalar ao lado, `"removable": false` precisa ser **explícito** — omitir a
  chave não é neutro.
- `bootloader` no topo (sem `bootloader_config`) ainda funciona, marcado como
  DEPRECATED, e nesse caminho `removable` é forçado a `true`.

## 3.3 `--silent` e `--dry-run`

Os dois existem (`lib/args.py`), mas **`--dry-run` não é o que o `design.md`
descreve**. Ajuda oficial: *"Generates a configuration file and then exits
instead of performing an installation"*.

Ordem real em `scripts/guided.py::main`:

1. `ArchConfigHandler()` — parse do JSON e desserialização nos modelos tipados.
   É aqui que erro de schema, valor de enum inválido e **alvo de disco que não
   resolve** aparecem.
2. `config.save()`.
3. `validate_bootloader_layout(bootloader_config, disk_config)` — validação
   cruzada bootloader × layout (UEFI-only em máquina BIOS, `/boot` não-FAT para
   Limine/Efistub).
4. `if args.dry_run: return` ← **sai aqui**.
5. `FilesystemHandler.perform_filesystem_operations()` — particionamento.

Ou seja: valida a configuração inteira e **não escreve um setor**, que é a
propriedade exigida pela spec `unattended-install`. O que ele **não** faz é
exercitar o particionamento em si — passar no dry-run não é prova de que a
instalação real funciona (o `design.md` já assume isso no risco correspondente).

`--silent` é ignorado se nem `--config` nem `--config-url` forem passados
(`_parse_args`), então ele nunca "automatiza sozinho" sem configuração.

## 3.4 Como o `manual_partitioning` referencia uma partição existente

**Esta é a descoberta que contradiz o `design.md`.**

`obj_id` **não identifica a partição no disco**. Em `PartitionModification` ele é
`_obj_id`, um `uuid.uuid4()` gerado pelo próprio archinstall, comentado no código
como *"special 'invisible' attr to internally identify the part mod"*, usado só
como chave de hash dentro do processo. Ao ler um config, o valor do JSON é
copiado para esse campo e nada mais.

O que de fato aponta para o disco:

- `device_modifications[].device` → `Path` passada a `device_handler.get_device()`,
  que é um `dict.get` por **caminho de dispositivo** (`/dev/nvme0n1`).
- `partitions[].dev_path` → casado por igualdade de string em
  `find_partition()` (`/dev/nvme0n1p1`).
- `status: "existing"` **exige** `dev_path` (`PartitionModification.__post_init__`
  levanta `ValueError` sem ele).
- `wipe` default `False` no parse — mas é explicitado mesmo assim (task 4.3).

`partuuid` aparece nos modelos apenas como campo **lido** do `lsblk` e reportado;
não há nenhum caminho de configuração que selecione partição por PARTUUID.

Consequência: o alvo é nomeado por caminho de kernel, que **não existe no
Windows** e não é derivável de lá — `/dev/sda` contra `/dev/nvme0n1` depende do
hardware e da ordem de enumeração. O mecanismo já resolvido em
`identify-disk-by-partuuid` não conecta aqui como o `design.md` supõe.

Falha, quando o caminho não resolve: `get_device` devolve `None` e o `for` faz
`continue` — a modificação some silenciosamente e a instalação segue sem
particionamento, falhando adiante. Não reparticiona outro disco por conta
própria (é o oposto do `partman`). Mas se `/dev/sda` existir e for **outro**
disco, ele é aceito sem questionar. Errar o caminho não é seguro — é só
silencioso de um jeito diferente.

## 3.5 Profiles de ambiente gráfico e greeter

`profile_config.profile = { "main": "Desktop", "details": ["<nome>"] }`, casado por
**nome exato** (`profile_handler.get_profile_by_name`).

Nomes disponíveis na 4.4 (`default_profiles/desktops/`): `Awesome`, `Bspwm`,
`Budgie`, `Cinnamon`, `Cosmic`, `Deepin`, `Enlightenment`, `GNOME`, `Hyprland`,
`i3-wm`, `Labwc`, `Lxqt`, `Mate`, `niri`, `niri - DankMaterialShell`,
`KDE Plasma`, `Qtile`, `River`, `Sway`, `Xfce4`, `Xmonad`.

`greeter` é o enum `GreeterType` pelo **valor**: `lightdm-gtk-greeter`,
`lightdm-slick-greeter`, `sddm`, `gdm`, `ly`, `cosmic-greeter`,
`plasma-login-manager`, `dms-greeter`.

Greeter padrão de cada perfil (é o que faz a sessão subir sozinha no primeiro
boot — task 7.5): Hyprland → `sddm`, GNOME → `gdm`, KDE Plasma →
`plasma-login-manager`.

## 3.6 Entrega do config à sessão live — **não resolvida**

O gancho existe e está documentado no código do `archiso` 89
(`configs/releng/airootfs/root/.automated_script.sh`, instalado com modo 0755 via
`profiledef.sh`, chamado por `/root/.zlogin`, só em `tty1`):

```bash
script=*)  echo "${param#*=}"          # lido de /proc/cmdline
# http|https|ftp|tftp  → baixa com curl via systemd-run (network-online.target)
# qualquer outra coisa → cp "${script}" /tmp/startup_script
chmod +x /tmp/startup_script && /tmp/startup_script
```

Aceita **URL** ou **caminho local**. E é aí que o plano trava:

- **URL** exige rede e um servidor. Não serve a um app offline no Windows.
- **Caminho local** é resolvido dentro do sistema de arquivos da sessão live,
  que vem do `airootfs.sfs` da ISO do fornecedor. O app, do lado do Windows,
  não tem onde gravar esse arquivo: a ISO não é modificável, e a partição-semente
  que o app já sabe criar (`CloudInitSeedWriter`) não está montada quando o
  `.zlogin` roda.

Ou seja: o `script=` existe, mas **nenhuma das duas formas tem transporte** a
partir do Windows sem uma peça nova. É exatamente o caso previsto na task 3.7 —
parar e redesenhar a entrega antes de escrever o gerador.

Direções que precisam ser avaliadas no `design.md` antes de qualquer código
(nenhuma verificada ainda):

1. Um cpio extra carregado pelo GRUB (o app já tem `UnattendedInitrdWriter` e
   `ExtraInitrdGrubPath`) que instale um hook de `mkinitcpio` com `run_latehook`
   gravando o script dentro de `${newroot}` — sobrevive ao `switch_root` porque
   escreve no overlay, não no initramfs (ver a memória sobre o casper).
2. O startup script montar a partição-semente ele mesmo — mas isso só desloca o
   problema, porque o script ainda precisa chegar à sessão live.
3. Gerar o JSON **no lado Linux**, dentro do script de startup, resolvendo o alvo
   por PARTUUID em runtime — o que também resolveria 3.4, já que `dev_path` só
   pode ser descoberto lá.

A direção 1 combinada com a 3 endereça as duas descobertas de uma vez, mas troca
"o app gera o JSON" (o que `proposal.md` e as tasks 4.2–4.6 descrevem) por "o app
gera um script que gera o JSON". É mudança de desenho, não de implementação.

## Estado das tasks da seção 3

- 3.1 ✅ — `archinstall 4.4-1`
- 3.2 ✅ — schema acima
- 3.3 ✅ — existem; `--dry-run` serve como portão, com a ressalva do escopo
- 3.4 ✅ — respondida, e **contradiz** a premissa do `design.md`
- 3.5 ✅ — profiles e greeters confirmados
- 3.6 ⚠️ — gancho confirmado no `archiso` que construiu a ISO; **transporte não
  resolvido**, e não confirmado dentro do `airootfs.sfs` desta build
- 3.7 → **acionada**: parar antes da seção 4
