## ADDED Requirements

### Requirement: Detectar as informações regionais a partir do Windows
O sistema SHALL derivar idioma, layout de teclado e fuso horário do próprio
Windows em que está rodando, e NÃO SHALL usar valores fixos no código para
nenhum dos três.

Um valor fixo não é um padrão razoável, é um erro silencioso: ele acerta apenas
para quem por acaso coincide com ele e erra para todos os demais, sem nunca se
anunciar. O sistema instalado é a primeira coisa que o usuário vê, e um teclado
errado é percebido na primeira senha digitada.

A detecção SHALL produzir valores no formato que o instalador Linux espera, e
não no formato do Windows — a conversão é responsabilidade do app, não do
usuário.

#### Scenario: Fuso do Windows é convertido para o formato do Linux
- **WHEN** o Windows está configurado em um fuso horário qualquer
- **THEN** a configuração de instalação carrega o fuso equivalente no formato
  que o sistema Linux instalado usa

#### Scenario: Layout de teclado acompanha o do Windows
- **WHEN** o Windows está com um layout de teclado configurado
- **THEN** a configuração de instalação carrega o layout equivalente, e não um
  valor fixo

#### Scenario: Idioma acompanha o do Windows
- **WHEN** o Windows está em um idioma qualquer
- **THEN** a configuração de instalação carrega o locale correspondente

#### Scenario: Detecção sem correspondência não inventa valor
- **WHEN** a configuração do Windows não tem equivalente conhecido no Linux
- **THEN** o sistema usa um padrão declarado e o apresenta ao usuário para
  revisão, em vez de gravar um valor arbitrário sem avisar

### Requirement: Permitir revisar as informações regionais antes de instalar
O wizard SHALL apresentar idioma, layout de teclado e fuso horário ao usuário,
já preenchidos com o que foi detectado, e SHALL permitir alterá-los antes da
instalação começar.

Detectar sem deixar revisar repete o problema em outra forma: a detecção pode
errar, e o usuário pode simplesmente querer outra coisa — um teclado físico
diferente do configurado no Windows é caso comum. Perguntar do zero, por outro
lado, cansa quem já tem o Windows configurado do jeito certo. O padrão detectado
com possibilidade de correção atende aos dois.

O valor exibido SHALL ser o que efetivamente vai para a instalação — não pode
haver divergência entre o que o usuário viu e o que foi gravado.

#### Scenario: Usuário aceita o que foi detectado
- **WHEN** o usuário chega ao passo de configuração regional e segue sem alterar
  nada
- **THEN** os valores detectados são os usados na instalação

#### Scenario: Usuário corrige um valor detectado
- **WHEN** o usuário altera o layout de teclado, o idioma ou o fuso
- **THEN** a instalação usa o valor escolhido por ele, não o detectado

### Requirement: Oferecer a escolha do ambiente gráfico quando o mecanismo permite
Para distros cujo mecanismo de instalação desatendida permite escolher o
ambiente gráfico, o wizard SHALL apresentar essa escolha; para as demais, NÃO
SHALL apresentá-la.

A escolha não existe universalmente: a maioria das ISOs do catálogo já embute um
ambiente gráfico, e oferecer uma opção que será ignorada é pior do que não
oferecer — promete ao usuário um controle que ele não tem.

A disponibilidade SHALL ser derivada do mecanismo declarado pela distro, nunca de
uma verificação do nome ou identificador dela.

#### Scenario: Distro cujo mecanismo permite escolher
- **WHEN** o usuário seleciona uma distro cujo mecanismo suporta escolha de
  ambiente gráfico e ativa a instalação automática
- **THEN** o wizard oferece a escolha, e o ambiente selecionado é o instalado

#### Scenario: Distro cuja ISO já define o ambiente
- **WHEN** o usuário seleciona uma distro cujo mecanismo não suporta essa escolha
- **THEN** o wizard não apresenta a opção

#### Scenario: Disponibilidade vem do mecanismo, não da identidade da distro
- **WHEN** uma nova distro passa a declarar um mecanismo que suporta a escolha
- **THEN** o wizard passa a oferecê-la sem que seja preciso alterar a lógica da
  interface
