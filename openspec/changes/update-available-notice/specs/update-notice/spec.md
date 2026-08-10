## ADDED Requirements

### Requirement: Comparar a versão em execução com a última release publicada
Na abertura do app, o sistema SHALL obter a versão da última release publicada
do projeto e compará-la com a versão do app em execução. A versão em execução
SHALL ser lida do próprio assembly, e não de um valor declarado à parte — duas
declarações da mesma versão divergem e o aviso passaria a mentir sobre o que o
usuário está rodando.

A comparação SHALL ser numérica sobre os três componentes da versão
(maior, menor, correção), nunca textual: `v1.10.0` é mais nova que `v1.9.0`,
embora seja menor na ordem alfabética.

Releases marcadas como rascunho ou pré-lançamento SHALL ser ignoradas — o aviso
só aponta para o que está publicado como estável.

#### Scenario: Versão em execução está atrás da publicada
- **WHEN** a última release publicada é mais nova que a versão do app em execução
- **THEN** o usuário é avisado de que existe versão nova disponível

#### Scenario: Versão em execução está em dia
- **WHEN** a versão do app em execução é igual à da última release publicada
- **THEN** nenhum aviso aparece e o app abre normalmente

#### Scenario: Versão em execução está à frente da publicada
- **WHEN** a versão do app em execução é maior que a da última release publicada
- **THEN** nenhum aviso aparece — uma build de desenvolvimento não é tratada como desatualizada

#### Scenario: Comparação numérica e não alfabética
- **WHEN** a versão em execução é `1.9.0` e a última publicada é `1.10.0`
- **THEN** o app é considerado desatualizado e o aviso aparece

### Requirement: O aviso informa e não obriga
O aviso SHALL comunicar que existe uma versão mais recente e que atualizar pode
corrigir problemas e melhorar a experiência, apresentando a versão em execução e
a versão disponível para que o usuário saiba o tamanho da diferença.

O aviso SHALL oferecer duas saídas: abrir a página da release nova, ou fechar
sem fazer nada. Ambas SHALL devolver o usuário ao app em funcionamento pleno —
atualizar é opcional e recusar não SHALL restringir nenhuma funcionalidade.

#### Scenario: Usuário opta por baixar
- **WHEN** o usuário escolhe baixar a nova versão
- **THEN** a página da release é aberta no navegador padrão e o aviso se fecha,
  deixando o app utilizável

#### Scenario: Usuário dispensa o aviso
- **WHEN** o usuário fecha o aviso sem baixar
- **THEN** o app segue plenamente utilizável, sem nenhuma funcionalidade bloqueada ou degradada

### Requirement: A checagem não atrasa nem bloqueia a abertura do app
A janela principal SHALL estar visível e utilizável antes que a checagem
termine. O usuário nunca SHALL esperar por rede para começar a usar o app, e uma
rede lenta ou que não responde nunca SHALL deixar o app travado na abertura.

O aviso, quando aparece, SHALL ser apresentado sobre a janela principal já
aberta, e não no lugar dela.

#### Scenario: Rede lenta não segura a abertura
- **WHEN** a consulta à última release demora ou não responde
- **THEN** a janela principal já está visível e utilizável durante toda a espera

#### Scenario: Aviso aparece sobre a janela principal
- **WHEN** a checagem conclui que há versão nova
- **THEN** o aviso é exibido sobre a janela principal já aberta

### Requirement: Falha de checagem é registrada, nunca exibida
Qualquer falha da checagem — ausência de rede, tempo esgotado, resposta de erro
do serviço, resposta ilegível, ou versão publicada fora do formato esperado —
SHALL ser registrada em log persistente com informação suficiente para
diagnóstico posterior, e SHALL ser invisível para o usuário: nenhuma mensagem de
erro, nenhum aviso, nenhuma interrupção do startup.

Estar sem internet é situação comum e esperada para este app, não um defeito; um
erro exibido nesse caso não geraria relato útil e ensinaria o usuário a fechar os
avisos do app sem ler — o que é perigoso justamente porque os outros avisos deste
app tratam de reparticionamento de disco.

O registro SHALL ir para um log próprio de falhas de rede, separado do log de
operações de instalação, para não poluir o material usado para diagnosticar boot
e disco quebrados.

#### Scenario: Sem conexão com a internet
- **WHEN** o app abre em uma máquina sem internet
- **THEN** nenhum erro é exibido, o app abre normalmente, e a falha fica registrada no log de falhas de rede

#### Scenario: Resposta ilegível ou versão em formato inesperado
- **WHEN** a consulta retorna algo que não pode ser interpretado como uma versão válida
- **THEN** nenhum aviso aparece ao usuário, e o motivo da falha fica registrado no log de falhas de rede

#### Scenario: Log de rede não se mistura ao de instalação
- **WHEN** uma falha de checagem é registrada
- **THEN** o registro vai para um arquivo próprio, e o log de operações de instalação permanece intocado

### Requirement: O aviso reaparece enquanto a versão não for atualizada
O sistema NÃO SHALL persistir qualquer marca de "aviso já visto". Enquanto o app
em execução continuar atrás da última release publicada, o aviso SHALL aparecer a
cada abertura; quando a versão em execução alcançar a publicada, ele SHALL parar
de aparecer sem nenhuma ação de limpeza.

A ausência de estado persistido é deliberada: a própria versão em execução já é a
fonte de verdade sobre o que precisa ser avisado, e guardar essa decisão à parte
criaria um segundo estado a manter em sincronia.

#### Scenario: Usuário dispensou e reabre o app
- **WHEN** o usuário dispensou o aviso e abre o app de novo, ainda na versão antiga
- **THEN** o aviso aparece novamente

#### Scenario: Usuário atualizou
- **WHEN** o usuário instalou a versão nova e abre o app
- **THEN** o aviso não aparece mais, sem exigir nenhuma limpeza de estado

### Requirement: Textos do aviso seguem o idioma do app
Todo texto do aviso voltado ao usuário — título, mensagem e rótulos dos botões —
SHALL vir da fonte única de strings localizadas do projeto, disponível em todos
os idiomas que o app oferece. Nenhum texto do aviso SHALL ser fixo no código.

#### Scenario: Aviso em português
- **WHEN** o app está em português e o aviso aparece
- **THEN** título, mensagem e botões estão em português

#### Scenario: Aviso em inglês
- **WHEN** o app está em inglês e o aviso aparece
- **THEN** título, mensagem e botões estão em inglês
