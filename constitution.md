# LinuxHub — Engineering Constitution

Este documento define os padrões inegociáveis de arquitetura e código para o
LinuxHub. Toda proposta de mudança (`openspec/changes/*`) e toda implementação
devem respeitar estes princípios. Se um princípio precisar ser quebrado, isso
é uma decisão de arquitetura e deve ser justificada explicitamente no
`design.md` da mudança — nunca feita silenciosamente no meio do código.

## 1. Arquitetura: Feature-based + MVVM

- O código é organizado por **feature** (o que o usuário faz), não por tipo de
  arquivo. Nada de pastas genéricas `Utils/`, `Helpers/` viradas depósito.
- Cada feature vive em `Features/<NomeDaFeature>/` com:
  - `Views/` — XAML + code-behind mínimo (só `InitializeComponent()`,
    wiring de `DataContext` e, quando estritamente necessário, código que só
    pode viver no code-behind por limitação do WPF — ex.: efeitos visuais).
  - `ViewModels/` — estado e lógica de apresentação. Implementam
    `INotifyPropertyChanged`. Nenhuma lógica de negócio pesada aqui além de
    orquestrar chamadas a services.
  - `Services/` — lógica de negócio e I/O (download, WMI, disco, geração de
    config). Livres de tipos de WPF sempre que possível, para serem
    testáveis sem UI.
- Código compartilhado entre features vive em `Common/` (`Models/`, `Data/`,
  `Mvvm/`), nunca duplicado por feature.
- **Nenhum evento de UI (`Click=`, `Checked=`) chama lógica de negócio
  diretamente.** Eventos disparam `ICommand`s expostos pela ViewModel.

## 2. SOLID

- **SRP** — uma classe, uma razão para mudar. Se um arquivo mistura "baixar
  ISO" + "enumerar disco" + "montar config", ele está errado, mesmo que
  tenha poucas linhas.
- **OCP** — adicionar uma distro nova ou um novo tipo de instalação não pode
  exigir editar um `switch`/`if` gigante espalhado pelo código. Prefira dados
  (catálogo) e estratégias/composição a condicionais acopladas à UI.
- **LSP** — hierarquias de tipos (quando existirem) devem ser substituíveis
  sem quebrar o comportamento esperado pelo consumidor.
- **ISP** — interfaces de service pequenas e focadas por feature
  (`IIsoDownloadService`, `IDiskInventoryService`), não uma `IAppService`
  genérica com 20 métodos.
- **DIP** — ViewModels dependem de abstrações (interfaces), injetadas via
  construtor. A composição concreta (qual implementação usar) acontece em um
  único lugar: a composition root (`App.xaml.cs`).

## 3. Zero duplicação (DRY)

- Dado que já existe uma vez no projeto, não é reescrito em outro lugar.
  Exemplo do problema histórico: catálogo de distros existia duas vezes
  (hardcoded no `MainWindow` e num dicionário no `DistroDetector`),
  divergentes entre si. Isso é proibido — fonte única de verdade.
- XAML repetitivo (o mesmo bloco copiado N vezes para N itens) é um bug de
  arquitetura, não "só XAML". Use `ItemsControl` + `DataTemplate` + binding a
  uma coleção.
- Antes de criar um novo service/model/helper, procure se já existe algo
  parecido no projeto. Um service morto (criado, nunca chamado) é pior que
  não ter service nenhum — ou se integra de verdade, ou se remove.

## 4. Clean Code

- Nomes revelam intenção; sem abreviações obscuras.
- Métodos pequenos, um nível de abstração por método.
- **Nenhum arquivo de code-behind ou ViewModel deve crescer sem limite.**
  Passar de ~250-300 linhas é sinal de que a classe está fazendo mais de uma
  coisa — pare e extraia antes de continuar adicionando.
- Comentários só para o "porquê" não-óbvio (uma constraint escondida, um
  workaround). Nunca para descrever o que o código já deixa claro.
- Sem código morto, sem `catch (Exception) { }` silencioso, sem
  "vou deixar comentado caso precise depois".
- **Nenhuma string voltada ao usuário é hardcoded em C# ou XAML** — nem em
  literais soltos, nem em dados estáticos tipo `DistroCatalog`. Todo texto que
  aparece na UI (labels, mensagens, bio de distro, nomes de botão) vive em
  `Common/Localization/Strings*.resx`, acessado via `{loc:Loc Chave}` no XAML
  ou `LocalizationManager.Instance["Chave"]` no C#. Quando o texto depende de
  um item de dado (ex.: descrição por distro), o dado guarda a **chave** do
  recurso (`DistroInfo.DescriptionKey`), nunca o texto em si — assim a UI
  troca de idioma em runtime sem editar o catálogo. Exceção: valores que não
  são prosa/linguagem (Id, Version, ano, URLs, nomes próprios como "Ubuntu")
  podem ficar como dado puro.

## 5. Testabilidade

- Regras de negócio (validação de ISO, detecção de distro, montagem de
  `InstallerConfig`) vivem em classes puras de C#, sem dependência direta de
  `System.Windows.*`, para poderem ser testadas sem precisar de UI.
- Operações perigosas (particionamento de disco, `bcdedit`, apagar dados)
  ficam atrás de uma interface para poderem ser substituídas por fakes em
  teste — nunca chamadas direto via `Process.Start` espalhado pela ViewModel.

## 6. Segurança e cuidado com o sistema do usuário

- Este app mexe em disco e partições do usuário. Qualquer operação
  destrutiva (apagar disco, redimensionar partição) precisa de confirmação
  explícita na UI antes de executar, e deve ser reversível até o ponto de
  não-retorno real.
- Nunca silenciar exceções em operações de disco/boot — o usuário precisa
  saber quando algo falhou.

### 6.1 Automação desatendida: falhar parando, nunca prosseguindo

Regras nascidas de um incidente real (2026-08-05): uma instalação automática
do Mint apagou a ESP e levou junto as entradas EFI de todas as outras distros
da máquina. Ver `openspec/changes/mint-ubiquity-autoinstall`.

- **Nunca ligar automação de particionamento sem o alvo garantido.** O perigo
  real não são as confirmações destrutivas em si — é preencher parcialmente as
  respostas que selecionam o disco. No partman, `partman-auto/method` com
  `partman-auto/disk` vazio faz o instalador **eleger sozinho** o disco quando a
  máquina só tem um (`display.d/10initial_auto`), e `method` sempre significa
  disco inteiro. Foi essa combinação que apagou a ESP no incidente. Um valor de
  alvo só é gravado se resolveu de verdade; nada de default implícito.
- **As confirmações destrutivas são a última rede — ligá-las é assumir que tudo
  antes está garantido.** `partman/confirm`,
  `partman-partitioning/confirm_write_new_label` e equivalentes só ficam
  ligadas depois que a seleção do alvo é comprovadamente à prova de falha; até
  lá, elas são o que faz o instalador parar em vez de destruir.
- **Automação incompleta é preferível a automação insegura.** Se uma etapa não
  pode ser automatizada com garantia, ela fica manual e o instalador pergunta.
  Nunca se preenche uma resposta "provável" para uma pergunta que decide o
  destino de um disco. Tentar um valor que, se não casar, faz o instalador
  perguntar (falha segura) é aceitável; tentar um que, se não casar, o faz
  escolher sozinho, não é.
- **Teste destrutivo é em VM com snapshot, nunca na máquina do usuário.** É lá
  que o caminho completo deve ser exercitado — e o que impede esse caminho de
  alcançar uma máquina real é a declaração no catálogo (§7.1), não o instalador
  parar no meio.
- **Nada que rode fora do Windows pode ser assumido presente.** Comando usado
  em script gerado para o initramfs, o live ou o chroot precisa ser verificado
  no artefato real (initrd/pacote/ISO) antes de ser usado. `lsblk` e
  `debconf-set` não existem no initramfs do casper — foi o que quebrou a
  identificação do disco no incidente.
- **Arquivo gravado no initramfs some no `switch_root`.** Qualquer coisa que o
  instalador vá ler depois dessa troca precisa viver no filesystem live ou numa
  partição, não só no initramfs.

## 7. Processo

- Antes de implementar qualquer mudança neste projeto, leia este documento.
  (Isso é reforçado por `.claude/rules/constitution.mdc`.)
- Mudanças de arquitetura relevantes passam por uma proposta em
  `openspec/changes/` antes da implementação.
- Esta constitution pode evoluir, mas mudanças nela são deliberadas — nunca
  um efeito colateral de uma mudança de feature.

### 7.1 Validação antes de declarar

- **Uma capacidade só é declarada no catálogo depois de validada num boot
  real.** `DistroInfo.UnattendedInstall` (e qualquer flag equivalente que
  habilite comportamento destrutivo) fica em `None` até existir a prova. Foi
  declarar antes que transformou código não testado em código rodando na
  máquina do usuário, no incidente de 2026-08-05.
- **Quando um plano define uma task como gate, o que vem depois dela fica
  desarmado em runtime.** Implementar adiante é aceitável; deixar alcançável
  não é. Autorização para fazer todas as tasks é autorização de esforço, não
  de risco — e quem executa o gate precisa saber que ele ainda não passou.
- **Risco anotado não é risco resolvido.** Um limite registrado em
  `TEST_MATRIX.md` ou num comentário não autoriza seguir como se ele não
  existisse; ou é fechado, ou o que depende dele fica desarmado.
