## ADDED Requirements

### Requirement: Declarar o mecanismo de instalação desatendida no catálogo
O catálogo SHALL declarar, para cada distro, qual mecanismo de instalação
desatendida aquela build usa, como parte da mesma fonte única de dados que já
descreve nome, família e versão. A declaração SHALL distinguir *qual*
mecanismo, e não apenas se existe algum — um booleano não é suficiente porque o
gerador precisa escolher entre formatos incompatíveis entre si.

O padrão para uma distro recém-adicionada SHALL ser "nenhum mecanismo": só uma
build efetivamente validada de ponta a ponta (geração, transporte, boot e
instalação real) pode declarar um mecanismo.

#### Scenario: Distro validada declara seu mecanismo
- **WHEN** uma build de distro foi validada de ponta a ponta com um mecanismo
  de instalação desatendida
- **THEN** o catálogo declara esse mecanismo para aquela distro, e o wizard
  oferece a instalação automática quando ela é a distro selecionada

#### Scenario: Distro não validada não promete automação
- **WHEN** uma distro do catálogo não teve nenhum mecanismo validado
- **THEN** o catálogo a declara sem mecanismo, e o wizard não oferece a opção
  de instalação automática para ela

#### Scenario: Versão do catálogo casa com a build validada
- **WHEN** o catálogo declara um mecanismo de instalação desatendida para uma
  distro
- **THEN** a versão e o link direto de download daquela entrada apontam para a
  mesma build em que o mecanismo foi validado
