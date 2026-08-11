## ADDED Requirements

### Requirement: O alvo do particionamento é nomeado, nunca inferido
Um mecanismo de instalação desatendida SHALL ser adotado apenas se permitir
identificar nominalmente cada partição envolvida — a que será criada e a que
será reaproveitada. O alvo SHALL ser nomeado por um identificador que vale
igualmente nos dois lados da instalação — o que o app conhece antes de reiniciar
e o que o instalador vê depois — e NÃO SHALL ser um nome atribuído pelo sistema
em execução, que só existe depois do boot e que o gerador teria que adivinhar.

Esta é a lição direta do incidente de 2026-08-05: um alvo preenchido pela metade
fez o instalador eleger o disco sozinho e reparticioná-lo por inteiro, apagando
a ESP e as entradas de boot de todos os outros sistemas da máquina.

Quando o mecanismo escolhido só souber endereçar partições por um nome atribuído
em tempo de execução, essa tradução SHALL partir do identificador estável que o
app nomeou, e SHALL acontecer no lado que consegue observá-la. Traduzir é
aceitável; adivinhar não é — um nome que por acaso exista e aponte para outro
disco é aceito sem questionar por qualquer instalador.

A configuração gerada SHALL declarar explicitamente que o dispositivo não deve
ser apagado quando a instalação for ao lado de um sistema existente.

#### Scenario: Instalação ao lado preserva o que já existe
- **WHEN** o usuário instala ao lado de um sistema existente
- **THEN** a configuração gerada identifica nominalmente a partição a criar e a
  partição de boot a reaproveitar, e declara que o dispositivo não deve ser
  apagado

#### Scenario: Alvo que não resolve interrompe em vez de escolher
- **WHEN** alguma partição envolvida não pode ser identificada com certeza
- **THEN** a instalação não prossegue, e em nenhuma hipótese o instalador
  escolhe um alvo por conta própria

#### Scenario: Mecanismo que endereça o disco por nome de tempo de execução
- **WHEN** o mecanismo exige o nome que o sistema em execução deu ao disco, e
  esse nome não é conhecível antes do boot
- **THEN** o app nomeia o alvo pelo identificador estável da partição, a
  tradução é feita já com a máquina ligada, e um identificador que não resolve
  interrompe a instalação em vez de seguir com um nome provável

### Requirement: Validar a configuração sem efeito destrutivo antes de executar
Antes de qualquer instalação real, o sistema SHALL ser capaz de submeter a
configuração gerada a uma validação que não escreve em disco, e essa validação
SHALL ser o portão de qualquer declaração de capacidade no catálogo.

Sem isso, a única forma de descobrir que uma configuração está errada é
executá-la — e o custo de estar errado é o disco do usuário. Um mecanismo que
não oferece esse modo não pode ser validado com segurança.

#### Scenario: Configuração inválida é detectada sem risco
- **WHEN** a configuração gerada contém um erro
- **THEN** a validação sem efeito destrutivo o revela, sem que nenhuma partição
  tenha sido alterada

#### Scenario: Validação precede a declaração no catálogo
- **WHEN** um mecanismo novo ainda não passou pela validação sem efeito
  destrutivo
- **THEN** a distro correspondente permanece sem mecanismo declarado, e o wizard
  não oferece instalação automática para ela

### Requirement: Escolha de ambiente gráfico faz parte da configuração gerada
Quando o mecanismo permite escolher o ambiente gráfico e o usuário fez uma
escolha, a configuração gerada SHALL declará-la, junto com o que for necessário
para que a sessão gráfica funcione no primeiro boot.

Instalar um ambiente gráfico sem o meio de iniciá-lo entrega uma máquina que
liga num terminal — para o público deste app, isso é indistinguível de uma falha.

#### Scenario: Ambiente escolhido chega ao sistema instalado
- **WHEN** o usuário escolhe um ambiente gráfico e a instalação conclui
- **THEN** o sistema instalado inicia nesse ambiente, sem exigir nenhum comando
  do usuário

### Requirement: O bootloader instalado não depende do tamanho da partição de boot existente
A configuração gerada SHALL usar um bootloader que funcione com a partição de
boot já existente na máquina, qualquer que seja o tamanho dela, e NÃO SHALL
exigir redimensioná-la.

A partição de boot criada pelo Windows costuma ser pequena e é a primeira do
disco: aumentá-la exigiria deslocar todas as partições seguintes, incluindo o
próprio Windows. O risco dessa operação é desproporcional ao ganho, e ela não
pode ser um pré-requisito da instalação.

#### Scenario: Partição de boot pequena não impede a instalação
- **WHEN** a máquina tem uma partição de boot pequena, já ocupada pelo sistema
  existente
- **THEN** a instalação conclui e o sistema inicia, sem que a partição precise
  ser redimensionada

#### Scenario: Partição de boot existente é preservada
- **WHEN** a instalação usa a partição de boot já existente
- **THEN** os arquivos de boot do sistema anterior permanecem intactos, e ambos
  os sistemas continuam inicializáveis
