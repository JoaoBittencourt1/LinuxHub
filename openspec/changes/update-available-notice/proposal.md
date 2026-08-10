## Why

O app não tem canal de comunicação com quem já o instalou. Quando uma correção
sai — e este é um app que reparticiona disco e mexe em boot, onde uma correção
pode ser exatamente o que evita um incidente na máquina do usuário — não existe
nada que avise o usuário de que ele está rodando uma versão antiga. A pessoa só
descobre se voltar ao GitHub por conta própria.

Este change fecha essa lacuna com o mínimo necessário: no startup, o app compara
sua versão com a da última release publicada e, se estiver atrás, avisa. Só
avisa — atualizar continua sendo escolha do usuário.

## What Changes

- Nova feature `Features/UpdateCheck/` com service, ViewModel e view próprios.
- No startup, consulta sem autenticação a última release do repositório
  `joaobittencourt1/linuxbit` na API do GitHub e compara com a versão do
  assembly local.
- Quando a versão local está atrás, um modal informa que há versão nova e que
  atualizar pode corrigir bugs e melhorar a experiência. O modal oferece abrir a
  página da release no navegador, ou fechar sem fazer nada.
- **Novo padrão de UI no projeto**: o modal é uma janela própria com o tema
  Fluent do WPF-UI. Os diálogos existentes usam `MessageBox.Show` nativo, que
  não acompanha o tema escuro do resto do app. Este change introduz o primeiro
  diálogo estilizado; os `MessageBox` existentes permanecem como estão (migrá-los
  não faz parte deste escopo).
- Falha de checagem nunca interrompe nem avisa o usuário: fica registrada num
  arquivo de log dedicado, separado do log de instalação.
- Nenhum estado é persistido. Enquanto o usuário não atualizar, o aviso reaparece
  a cada abertura — o que dispensa qualquer mecanismo de preferências, que o
  projeto não tem hoje.
- Sem breaking changes. Nada do fluxo existente (catálogo, wizard, instalação)
  muda de comportamento.

## Capabilities

### New Capabilities
- `update-notice`: detectar que existe versão mais recente publicada e informar
  o usuário de forma dispensável, sem bloquear o uso do app e sem interromper o
  startup quando a checagem falha.

### Modified Capabilities
<!-- Nenhuma. As capabilities existentes (distro-catalog, install-wizard) não
     têm requisitos alterados por este change. -->

## Impact

**Código novo**
- `Features/UpdateCheck/Services/` — contrato da checagem, implementação contra
  a API do GitHub, e o parser de versão (classe pura, testável sem rede).
- `Features/UpdateCheck/ViewModels/` — estado do aviso e o comando de abrir a
  release.
- `Features/UpdateCheck/Views/` — o diálogo.
- `Common/Diagnostics/` — um log dedicado a falhas de rede, ao lado do
  `DiagnosticLog` existente.

**Código alterado**
- `App.xaml.cs` — a composition root passa a construir o service e a disparar a
  checagem depois que a janela principal já apareceu.
- `Common/Localization/Strings.resx` e `Strings.en-US.resx` — textos do diálogo
  nos dois idiomas.

**Dependências**
- Nenhum pacote novo. `HttpClient` da BCL e o WPF-UI 4.3 já referenciado.

**Externo**
- Passa a existir uma chamada de rede a `api.github.com` na abertura do app.
  Sem autenticação, sujeita ao rate limit por IP da API pública do GitHub.
- Cria um acoplamento operacional novo: o aviso só funciona se as releases do
  repositório continuarem sendo publicadas com tags no formato `vX.Y.Z` e se a
  versão do `.csproj` for mantida em sincronia com a tag a cada release.

**Testes**
- `tests/LinuxHub.Tests/Features/UpdateCheck/` — cobertura do parser de versão e
  da regra de comparação, sem rede.
