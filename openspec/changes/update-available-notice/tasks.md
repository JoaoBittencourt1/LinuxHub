## 1. Comparação de versão (lógica pura, sem rede)

- [x] 1.1 Criar `Features/UpdateCheck/Services/ReleaseVersionParser.cs`: recebe a tag (`"v1.2.4"`), remove o prefixo `v` e devolve um `Version` de exatamente três componentes. Entrada fora do formato não retorna versão silenciosamente — sinaliza a falha para quem chamou.
- [x] 1.2 Adicionar ao mesmo arquivo a regra de "está desatualizado": normaliza a versão local para três componentes (descartando o Revision do assembly) antes de comparar, conforme decisão 3 do `design.md`. Sem essa normalização `Version(1,2,4)` fica **menor** que `Version(1,2,4,0)` e a comparação mente.
- [x] 1.3 Criar `tests/LinuxHub.Tests/Features/UpdateCheck/ReleaseVersionParserTests.cs` (xUnit) cobrindo os cenários do spec: local atrás → desatualizado; local igual → em dia; local à frente → em dia; `1.9.0` vs `1.10.0` → desatualizado (comparação numérica, não alfabética).
- [x] 1.4 Incluir teste explícito de igualdade entre assembly de 4 componentes (`1.2.4.0`) e tag de 3 (`v1.2.4`) — é o caso que a decisão 3 existe para proteger e que passaria despercebido sem teste dedicado.
- [x] 1.5 Incluir testes de tag malformada (`"1.2.4"` sem `v`, `"v1.2"`, `"vabc"`, string vazia) confirmando que o parser sinaliza falha em vez de devolver versão inventada.
- [x] 1.6 Rodar `dotnet test` e confirmar que a suíte inteira (nova e existente) passa.

## 2. Log de falhas de rede

- [x] 2.1 Criar `Common/Diagnostics/HttpErrorLog.cs` escrevendo em `%LocalAppData%\LinuxHub\logs\http_erros.log`, espelhando a mecânica de `DiagnosticLog` (lock de escrita, `Directory.CreateDirectory`, tolerar `IOException`/`UnauthorizedAccessException` sem propagar).
- [x] 2.2 Documentar no XML-doc por que é um arquivo separado do `install-{data}.log` e não uma seção dele (decisão 7 do `design.md`): ruído de rede de todo startup degradaria o material usado para diagnosticar boot/disco quebrados.
- [x] 2.3 Garantir que a entrada registrada carrega o suficiente para diagnóstico: momento, o que se tentou (URL), e a exceção ou o status HTTP recebido.

## 3. Service de checagem

- [x] 3.1 Criar `Features/UpdateCheck/Services/IUpdateCheckService.cs` — método assíncrono devolvendo a versão da última release e a URL dela. Nenhum tipo de `System.Windows` na assinatura (§5 da constitution).
- [x] 3.2 Criar `Features/UpdateCheck/Services/GitHubUpdateCheckService.cs` consultando `https://api.github.com/repos/joaobittencourt1/linuxbit/releases/latest`, desserializando `tag_name` e `html_url` com `System.Text.Json` num record mínimo.
- [x] 3.3 Definir o header **`User-Agent`** na requisição. **Sem ele o GitHub responde 403 e o recurso falha em 100% das execuções** — e, como a falha é invisível por desenho, falharia sem nenhum sintoma. Definir também `Accept: application/vnd.github+json`.
- [x] 3.4 Definir timeout explícito (~10s) no `HttpClient`; o default de 100s é inadequado para uma tarefa de startup.
- [x] 3.5 Confirmar que o service **não** captura exceção genérica e **não** conhece UI: ele deixa a falha propagar para o ponto de orquestração (decisão 5 do `design.md`; §4 proíbe `catch` silencioso).

## 4. Textos localizados

- [x] 4.1 Adicionar em `Common/Localization/Strings.resx` (pt-BR) as chaves do diálogo: título, mensagem (nova versão disponível; atualizar pode corrigir problemas e melhorar a experiência), rótulo do botão de baixar e do botão de dispensar.
- [x] 4.2 Adicionar as **mesmas chaves** em `Common/Localization/Strings.en-US.resx`. Chave presente em um só arquivo cai no fallback silencioso do `LocalizationManager` (devolve a própria chave).
- [x] 4.3 Se a mensagem exibir a versão atual e a disponível, usar placeholder de formatação e `LocalizationManager.Instance.Format(...)` — nunca concatenar string traduzida com valor.

## 5. Diálogo de aviso

- [x] 5.1 Criar `Features/UpdateCheck/ViewModels/UpdateNoticeViewModel.cs` expondo versão atual, versão disponível e um `ICommand` para abrir a release (herdando de `ObservableObject`/`RelayCommand` de `Common/Mvvm`).
- [x] 5.2 No comando de abrir: validar que a URL é absoluta e de esquema `http`/`https` **antes** de `Process.Start(UseShellExecute = true)`; fora disso, registrar no log de rede e não abrir (decisão 9 do `design.md` — a URL vem de resposta externa).
- [x] 5.3 Criar `Features/UpdateCheck/Views/UpdateNoticeDialog.xaml` como janela WPF-UI seguindo o tema Fluent do app, com os dois botões ligados por `Command` (§1 proíbe `Click=` chamando lógica de negócio) e todos os textos via `{loc:Loc Chave}`.
- [x] 5.4 Manter o code-behind no mínimo permitido por §1 (`InitializeComponent`, `DataContext`); fechar o diálogo após a ação de baixar.

## 6. Ligação no startup

- [x] 6.1 Em `App.xaml.cs`, construir o service junto aos demais na composition root, mantendo o padrão de injeção por construtor já usado.
- [x] 6.2 Disparar a checagem **depois** de `mainWindow.Show()`, de forma assíncrona, para que a janela apareça e fique utilizável sem esperar a rede.
- [x] 6.3 Voltar para a thread de UI (`Dispatcher`) antes de abrir o diálogo, com `Owner` na janela principal.
- [x] 6.4 Tratar o caso de a janela principal já ter sido fechada quando a resposta chega — atribuir `Owner` a janela fechada lança; nesse caso, desistir do aviso silenciosamente.
- [x] 6.5 Concentrar aqui o `try/catch` que registra a falha em `HttpErrorLog` e segue sem exibir nada. Garantir que a tarefa assíncrona tem suas exceções observadas — nada de `async void` solto deixando exceção se perder antes de chegar ao log.
- [x] 6.6 Confirmar que quando não há versão nova nada é exibido, e que `App.xaml.cs` continua legível (a orquestração é curta ou está extraída para um método com nome próprio).

## 7. Validação real

- [x] 7.1 Rodar `dotnet build` e `dotnet test` — build limpo e suíte verde.
- [x] 7.2 **Teste de caminho feliz com versão rebaixada de propósito**: baixar temporariamente o `<Version>` do `.csproj` para algo abaixo da release publicada, abrir o app e confirmar que o diálogo aparece sobre a janela principal, com os textos corretos, e que "baixar" abre a página da release no navegador e fecha o diálogo. Reverter o `.csproj` depois.
- [x] 7.3 Confirmar que com o `.csproj` na versão real (em dia) nenhum diálogo aparece.
- [x] 7.4 **Teste de falha**: abrir o app sem internet e confirmar que nada é exibido, o app abre normalmente, e a falha aparece em `%LocalAppData%\LinuxHub\logs\http_erros.log`. Este passo é o único que distingue "funcionando" de "quebrado em silêncio" — sem ele, um `User-Agent` faltando ou URL errada passariam despercebidos.
- [ ] 7.5 Verificar o diálogo nos dois idiomas (PT e EN), confirmando que nenhum texto aparece como nome de chave crua.
- [x] 7.6 Confirmar no log de rede que a chamada real ao GitHub retorna 200 (e não 403 por falta de `User-Agent`) — inspecionando o log após um startup com internet.
- [x] 7.7 Confirmar que `install-{data}.log` não recebeu nenhuma entrada de checagem de atualização.
- [ ] 7.8 Registrar no processo de release que o `<Version>` do `.csproj` deve ser atualizado junto com a tag `vX.Y.Z` — é o risco de maior probabilidade do `design.md` e falha em silêncio nos dois sentidos.
