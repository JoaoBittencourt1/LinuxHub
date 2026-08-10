## Context

O app é distribuído como release no GitHub (`joaobittencourt1/linuxbit`) e não
tem nenhum canal para avisar quem já instalou que saiu versão nova. Ver
`proposal.md` para a motivação.

Estado atual relevante:

- **Composition root manual** em `App.xaml.cs` — sem container de DI, por decisão
  registrada no change `restructure-feature-based-mvvm`. Services concretos são
  construídos ali e injetados por construtor.
- **Versão já exposta na UI**: `Shell/MainWindow.xaml.cs:37-38` lê
  `Assembly.GetExecutingAssembly().GetName().Version` e formata como
  `v{Major}.{Minor}.{Build}` — exatamente o formato das tags de release. A fonte
  da versão local já existe e já está no formato certo.
- **Diálogos hoje são `MessageBox.Show` nativo**
  (`Features/InstallWizard/Views/InstallWizardView.xaml.cs:35`,
  `Features/Catalog/Views/DistroDetailView.xaml.cs:48`) — sem tema Fluent.
- **Log persistente existe**: `Common/Diagnostics/DiagnosticLog.cs` escreve em
  `%LocalAppData%\LinuxHub\logs\install-{data}.log`, e seu propósito declarado é
  diagnosticar operações elevadas (bcdedit, diskpart, ESP) depois de um reboot.
- **Não existe mecanismo de settings/preferências** no projeto.
- **Sem cliente HTTP compartilhado**: `IsoDownloadService` é o único ponto que
  fala HTTP hoje, e não fala com o GitHub.

Restrições vindas de `constitution.md`: §1 (feature-based, MVVM, nenhum evento de
UI chamando lógica de negócio), §4 (nenhuma string de UI fixa no código), §5
(regra de negócio em classe pura, testável sem UI; service livre de tipos WPF).

## Goals / Non-Goals

**Goals:**

- Detectar de forma confiável que a versão em execução está atrás da última
  release publicada, e avisar sem obrigar.
- Nunca degradar a abertura do app: nem atraso, nem travamento, nem erro na cara
  do usuário quando a rede falha.
- Manter a regra de comparação de versão testável sem rede.
- Estabelecer um padrão de diálogo estilizado que futuras telas possam reusar.

**Non-Goals:**

- **Não** baixar, instalar ou aplicar a atualização. O app abre a página da
  release no navegador; o download e a instalação são manuais.
- **Não** migrar os `MessageBox.Show` existentes para o novo diálogo. Fica para
  um change próprio.
- **Não** checar atualização fora do startup (nem periodicamente, nem sob
  demanda por um botão).
- **Não** persistir estado ("não avisar de novo", última checagem, versão
  ignorada).
- **Não** criar um cliente HTTP genérico ou camada de rede compartilhada. O
  escopo é uma chamada só.
- **Não** suportar pré-lançamentos, canais beta ou downgrade.

## Decisions

### 1. Feature própria em `Features/UpdateCheck/`, não um helper em `Common/`

Tem view, ViewModel e service — é uma feature pela definição de §1. `Common/`
existe para o que é compartilhado entre features, e isto não é consumido por
nenhuma outra.

*Alternativa descartada:* um `UpdateChecker` estático em `Common/`. Seria menos
arquivo, mas §1 proíbe pastas-depósito e o resultado não seria testável nem
substituível.

### 2. Separar o parser de versão do cliente HTTP

`ReleaseVersionParser` é classe pura: recebe a string da tag, devolve a versão.
Sem rede, sem WPF, sem I/O. O `GitHubUpdateCheckService` faz a chamada e delega o
parse.

§5 exige regra de negócio testável sem UI, e a regra que realmente pode dar
errado aqui é a comparação de versão — não o HTTP. Com a separação, todos os
cenários de comparação do spec viram teste unitário direto, sem mock de rede.

### 3. Normalizar as versões para três componentes antes de comparar

**Esta é a armadilha central deste change.** `System.Version` compara quatro
componentes, e um componente ausente vale `-1`, não `0`:

```
Assembly de <Version>1.2.4</Version>  →  Version(1, 2, 4, 0)
Tag "v1.2.4" parseada como 3 números  →  Version(1, 2, 4)   [Revision = -1]

Version(1,2,4) < Version(1,2,4,0)   →   true  (!!)
```

Comparadas cruas, a versão local pareceria **mais nova** que uma tag idêntica.
No caso "em dia" isso dá o resultado certo por acidente (nenhum aviso), mas
mascara o bug — que apareceria em qualquer lógica futura que dependa da
igualdade.

**Decisão:** os dois lados são normalizados para `Version(Major, Minor, Build)`
com exatamente três componentes antes de qualquer comparação, descartando o
Revision do assembly. O parser devolve três componentes e a versão local é
reconstruída com três. A igualdade exata entra na bateria de testes.

### 4. Endpoint e requisitos da chamada

`GET https://api.github.com/repos/joaobittencourt1/linuxbit/releases/latest`,
sem autenticação.

- **`/releases/latest` já exclui rascunhos e pré-lançamentos** — atende esse
  requisito do spec sem código de filtro.
- **Header `User-Agent` é obrigatório.** A API do GitHub responde **403 sem
  ele**. Sem esse header o recurso falharia em 100% das execuções, e — como
  falha é silenciosa por decisão (§7) — falharia *sem sintoma visível*. Precisa
  estar coberto por revisão.
- **Header `Accept: application/vnd.github+json`** para fixar a versão do
  formato da resposta.
- **Timeout curto e explícito** (~10s). O default do `HttpClient` é 100 segundos
  — cedo demais para não importar, tarde demais para uma tarefa de startup.
- Da resposta interessam dois campos: `tag_name` e `html_url`. Desserialização
  com `System.Text.Json` (BCL, sem pacote novo) num record mínimo.

### 5. O service devolve resultado; quem mostra UI é o startup

`IUpdateCheckService` devolve algo como
`UpdateCheckResult(Version Latest, Uri ReleaseUrl)` — sem tipo de `System.Windows`
em lugar nenhum, conforme §5. Ele **não** decide mostrar diálogo e **não**
engole exceção (§4 proíbe `catch` silencioso): deixa propagar.

A política "falhou? loga e segue" vive num único ponto de orquestração no
caminho de startup. Concentrar isso em um lugar é o que impede o `catch`
genérico de se espalhar pelo service.

### 6. Disparo no startup: janela primeiro, checagem depois

Em `App.OnStartup`, `mainWindow.Show()` continua acontecendo antes de qualquer
coisa de rede. A checagem roda de forma assíncrona depois disso, e o resultado
volta para a thread de UI pelo `Dispatcher` antes de abrir o diálogo.

Dois detalhes que precisam de cuidado explícito na implementação:

- **A janela pode já ter sido fechada** quando a resposta chega (usuário abriu e
  fechou rápido, ou a rede demorou). Atribuir `Owner` a uma janela fechada
  lança. O caminho precisa verificar isso e simplesmente desistir do aviso.
- **Nada de `async void` solto.** A continuação precisa ter suas exceções
  observadas para chegar ao log — uma exceção perdida numa tarefa esquecida é
  exatamente a falha silenciosa que §4 proíbe.

### 7. Falha vai para um log de rede próprio, não para o `DiagnosticLog`

Falhas de checagem (rede ausente, DNS, timeout, 4xx/5xx, JSON ilegível, tag fora
do formato) são gravadas em `%LocalAppData%\LinuxHub\logs\http_erros.log`.

*Por que não reusar `DiagnosticLog`* (o que §3/DRY sugeriria à primeira vista):
o arquivo dele é `install-{data}.log` e seu propósito declarado é diagnosticar
boot e disco quebrados depois de um reboot. Misturar ruído de rede de todo
startup nesse arquivo degrada justamente o material usado no diagnóstico mais
crítico do app. São dois destinos com públicos e ciclos de vida diferentes.

A mecânica (`%LocalAppData%\LinuxHub\logs\`, lock de escrita, tolerar
`IOException`/`UnauthorizedAccessException` sem abortar) segue o mesmo padrão já
estabelecido por `DiagnosticLog` — o que se separa é o destino, não o modo de
escrever.

*Nota de convenção:* o nome `http_erros` foi escolhido pelo usuário. Ele desvia
da convenção do log existente (`install-{data}.log`: inglês, hífen, com data). O
desvio foi levantado e aceito conscientemente.

### 8. Diálogo próprio com WPF-UI, e por quê

`MessageBox.Show` nativo não acompanha o tema Fluent escuro que o app aplica em
`App.OnStartup` — o aviso sairia visualmente destoante do resto. Este change
introduz o primeiro diálogo estilizado do projeto, usando o WPF-UI 4.3 já
referenciado (sem dependência nova).

- Botões ligados a `ICommand` da ViewModel, **não** a `Click=` com lógica no
  code-behind (§1). Code-behind fica no mínimo permitido.
- "Baixar" abre a URL e fecha o diálogo; "Depois" só fecha.
- Sem logo do produto: não existe asset de logo usável (só `favicon(N).ico` em
  `Assets/Icons/`, e nenhum PNG/SVG da marca).
- Título, mensagem e rótulos vêm de `Strings.resx` **e** `Strings.en-US.resx`
  (§4). A URL da release é dado puro, isento de localização por §4.

### 9. Abrir o navegador: validar o esquema da URL antes

A URL vem de uma resposta de rede. Passá-la direto para
`Process.Start(UseShellExecute = true)` significa entregar ao shell do Windows um
valor de origem externa — e `UseShellExecute` honra outros esquemas além de http.

**Decisão:** só abrir se a URL for absoluta e o esquema for `http` ou `https`;
caso contrário, tratar como falha e registrar no log. É uma checagem barata, e
este é um app que já roda operações elevadas — não é o lugar para relaxar com
entrada externa.

### 10. Sem persistência

A versão em execução já é a fonte de verdade sobre o que precisa ser avisado.
Guardar "já vi" criaria um segundo estado a manter em sincronia, e obrigaria a
inventar o primeiro mecanismo de preferências do projeto. O custo do aviso
reaparecer é baixo e ele para sozinho quando o usuário atualiza.

## Risks / Trade-offs

**A versão do `.csproj` sai de sincronia com a tag da release** → é o risco mais
provável de todos, porque depende de disciplina humana a cada release, e falha em
silêncio nos dois sentidos: `.csproj` atrás da tag avisa todo mundo sem motivo;
à frente, nunca avisa ninguém. *Mitigação:* documentar o passo no processo de
release e verificar na primeira validação real.

**Rate limit da API pública do GitHub (60 req/h por IP, sem auth)** → em IP
compartilhado (NAT corporativo, laboratório, sala de aula) o 403 é plausível.
*Mitigação:* nenhuma ação — é falha suave por desenho: nada é exibido, fica no
log, o app segue. Autenticar exigiria embutir token, o que é pior.

**Falha silenciosa esconde um recurso quebrado** → se o `User-Agent` faltar, ou a
URL do repo estiver errada, o recurso nunca funciona e ninguém percebe, porque o
comportamento de "sem novidade" e o de "quebrado" são idênticos para o usuário.
*Mitigação:* validar num teste manual real (com versão local rebaixada de
propósito) antes de considerar pronto; o log de rede é o que dá o veredito.

**Chamada de rede na abertura é dado que sai da máquina** → o app passa a
contatar `api.github.com` em todo startup, expondo IP e um `User-Agent`.
*Trade-off aceito:* é o mínimo para o recurso existir, sem telemetria nem
identificador do usuário.

**Novo padrão de UI conviverá com o antigo** → o projeto ficará com diálogos
Fluent e `MessageBox` nativos ao mesmo tempo, uma inconsistência visível.
*Trade-off aceito:* migrar os existentes é escopo próprio; este change entrega o
padrão para o próximo reusar.

**Um diálogo a mais na abertura é atrito recorrente** → quem escolhe não
atualizar vê o aviso toda vez. *Mitigação:* é consequência direta da decisão 10 e
foi escolhida de olho aberto; o aviso é dispensável em um clique e para sozinho
ao atualizar.

## Open Questions

Nenhuma. As decisões em aberto durante a exploração (logo no diálogo, destino do
log, comportamento dos botões, tratamento de falha, nome da mudança) foram todas
resolvidas e estão registradas acima.
