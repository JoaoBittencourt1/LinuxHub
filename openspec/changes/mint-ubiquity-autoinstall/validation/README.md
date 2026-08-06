# Validação em VM — instalação desatendida do Mint via Ubiquity

Como testar o caminho completo e o que capturar. O objetivo é fechar as tasks
2.2 (transporte do preseed), 5b.6 (qual resposta o particionamento aceita) e
6.3/6.4 (instalação real nos dois modos).

## Antes de tudo: VM com snapshot

Este change já destruiu a ESP de uma máquina real (ver `design.md`). Todo teste
acontece em VM, com snapshot tirado **antes** do primeiro boot — não porque o
problema conhecido continua aberto, mas porque a parte que ainda não se sabe (a
condução do partman) é a que decide o destino de um disco.

## Quem gera o cpio

`CpioArchiveWriter` (C#), o mesmo do fluxo de produção — não existe ferramenta
separada. O `UbiquityInstallPreparer` grava o arquivo ao lado da ISO
(`linuxhub-preseed.cpio`, no dual-boot) ou na raiz da partição de staging (no
substituir), e a entrada de boot já o referencia sozinha.

Durante a implementação existiu um script Python que montava esse cpio à mão,
para provar o formato antes do escritor em C# existir. Foi removido depois que a
saída dos dois se mostrou **byte a byte idêntica**: manter duas implementações do
mesmo formato — uma delas numa linguagem que não pertence ao projeto — só criaria
uma para divergir da outra.

## Como rodar

O Mint está deliberadamente em `None` no catálogo: o app não oferece instalação
automática para ele enquanto não houver boot real (`constitution.md` §7.1). Para
testar, declarar temporariamente em `Common/Data/DistroCatalog.cs`:

```csharp
UnattendedInstall = UnattendedInstallMechanism.UbiquityPreseed,
```

**Só ligue dentro da VM**, e não comite essa linha até 6.3/6.4 passarem.

Depois é o fluxo normal do app: escolher o Mint, ligar a instalação automática,
preencher a conta, escolher o modo e reiniciar.

## O que esperar

A automação está ligada por completo — a ideia é exercitar o caminho inteiro e
capturar log, não parar no meio.

| Modo | Esperado |
|---|---|
| **Substituir** | Instala sozinho do começo ao fim. `partman-auto/method=regular` + alvo resolvido caem no `autopartition` do disco inteiro, sem tela de particionamento |
| **Dual-boot** | Ou instala sozinho (se o partman casar `biggest_free` pelo nome do diretório), **ou** para só na tela de escolha do particionamento |

Parar na tela de particionamento no dual-boot é **falha segura e esperada** — o
valor real daquela escolha é um id `$dev//$id` calculado em runtime, e mandar
`biggest_free` é uma tentativa. Se parar, anotar as opções oferecidas: é o que
fecha a task 5b.6.

O que **não** pode acontecer em dual-boot é o instalador escolher um disco
sozinho e reparticionar. `partman-auto/method` não é emitido nesse modo
justamente para fechar esse caminho (era ele, com o alvo vazio, que causou o
incidente). Se acontecer mesmo assim, pare — significa que existe outra via que
não foi encontrada na leitura do partman.

## O que capturar

Da sessão live, com a instalação terminada ou travada:

```sh
# o preseed foi aplicado?
ls -la /LINUXHUB_PRESEED_OK 2>/dev/null   # só existe se um marcador foi usado
ls -la /linuxhub.recipe                   # a receita chegou ao filesystem live?

# o alvo foi resolvido e gravado?
debconf-get-selections | grep -E 'partman-auto/(disk|method|init_automatically)'

# os logs que importam
cp -r /var/log/installer /var/log/syslog /media/<compartilhamento>/
```

`/var/log/installer/debug` mostra cada pergunta de debconf e a resposta usada —
é ali que se lê qual valor o `automatically_partition` aceitou.

## Como ler as falhas

Cada sintoma aponta para um elo diferente da cadeia:

- **`/linuxhub.recipe` não existe no live** → o `early_command` não rodou ou
  falhou antes do `cp`. Conferir no `syslog` se o `24preseed` executou.
- **A receita existe mas `partman-auto/disk` está vazio** → o `blkid` não achou a
  partição semente. O comando só grava sob `[ -b "$d" ]`, então o vazio aqui é o
  comportamento correto, não um bug — a causa está na resolução.
- **O instalador pediu conta/senha** → o preseed não foi aplicado de jeito
  nenhum; o problema é o transporte (cpio/initrd), não o conteúdo.
- **Kernel panic ou não chega ao live** → o cpio extra quebrou o initramfs;
  suspeitar de alinhamento/formato antes de suspeitar do GRUB.
