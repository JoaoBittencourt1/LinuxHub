## ADDED Requirements

### Requirement: O link direto aponta para uma build ainda publicada
Cada entrada do catálogo que oferece download direto SHALL apontar para um
arquivo que ainda existe no servidor de origem. Uma entrada cujo link caducou é
uma falha visível para o usuário — ele escolhe a distro, inicia o download e
recebe um erro — e o catálogo não tem como perceber isso sozinho.

Distros cujo repositório mantém apenas as últimas versões SHALL ter sua entrada
revisada antes que a build declarada saia do ar. O prazo dessa revisão é
propriedade da distro, não do catálogo: algumas mantêm suas imagens por anos,
outras por poucos meses.

Usar um endereço genérico que sempre serve a versão mais recente NÃO SHALL ser
adotado para distro que declare mecanismo de instalação desatendida, porque
quebraria a correspondência exigida entre a versão validada e a versão
efetivamente entregue ao usuário.

#### Scenario: Build removida do servidor
- **WHEN** a build declarada por uma entrada deixa de estar publicada
- **THEN** a entrada é atualizada para uma build disponível, e a versão declarada
  acompanha a mudança

#### Scenario: Distro com retenção curta
- **WHEN** uma distro publica versões novas com frequência e mantém apenas as
  últimas
- **THEN** a revisão dessa entrada faz parte do processo de release do app, e não
  espera o usuário relatar o erro

#### Scenario: Link genérico não substitui a versão fixada
- **WHEN** uma distro declara mecanismo de instalação desatendida
- **THEN** seu link direto aponta para a build exata em que o mecanismo foi
  validado, e não para um endereço que serve sempre a versão mais recente
