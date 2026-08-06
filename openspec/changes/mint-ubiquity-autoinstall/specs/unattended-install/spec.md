## ADDED Requirements

### Requirement: Declarar o mecanismo de instalação desatendida por distro
O sistema SHALL tratar "instalação desatendida" como um mecanismo nomeado,
determinado pela build de distro selecionada, e não como uma capacidade única e
implícita. Os mecanismos reconhecidos SHALL ser `Subiquity` (autoinstall
cloud-init, instaladores subiquity) e `UbiquityPreseed` (preseed debconf,
instaladores Ubiquity), além do estado `None` para builds sem mecanismo
validado de ponta a ponta.

#### Scenario: Distro com instalador subiquity
- **WHEN** a distro selecionada é uma build declarada como `Subiquity`
- **THEN** o sistema usa o gerador de autoinstall cloud-init para produzir a
  configuração desatendida

#### Scenario: Distro com instalador Ubiquity
- **WHEN** a distro selecionada é uma build declarada como `UbiquityPreseed`
- **THEN** o sistema usa o gerador de preseed debconf para produzir a
  configuração desatendida

#### Scenario: Distro sem mecanismo validado
- **WHEN** a distro selecionada é declarada como `None`
- **THEN** o sistema não oferece a opção de instalação automática, e o boot é
  preparado apenas até o instalador nativo da ISO

### Requirement: Gerar preseed debconf para instaladores Ubiquity
Para uma distro com mecanismo `UbiquityPreseed`, o sistema SHALL gerar um
arquivo de preseed debconf que cubra o mesmo conjunto de dados que o caminho
subiquity já cobre: conta de usuário com senha em hash (nunca em texto claro),
nome do computador, locale, timezone, layout de teclado, o plano de
particionamento correspondente ao modo de instalação escolhido, e o reboot ao
final da instalação sem esperar remoção de mídia.

#### Scenario: Senha nunca viaja em texto claro
- **WHEN** o sistema gera o preseed para uma conta com senha informada no wizard
- **THEN** o preseed contém o hash da senha (`passwd/user-password-crypted`) e
  não contém a senha em texto claro em nenhuma diretiva

#### Scenario: Modo dual-boot preserva as partições existentes
- **WHEN** o modo de instalação é dual-boot
- **THEN** a receita de particionamento do preseed usa o espaço livre liberado
  para o Linux e não declara nenhuma operação que apague partições existentes

#### Scenario: Modo substituir usa o disco alvo identificado
- **WHEN** o modo de instalação é substituir e o disco alvo foi identificado
- **THEN** a receita de particionamento do preseed aponta para esse disco, e
  não para um índice de dispositivo assumido

#### Scenario: Instalação termina sem prompt de mídia
- **WHEN** a instalação desatendida via preseed chega ao fim
- **THEN** o sistema reinicia sem exibir prompt pedindo para remover a mídia de
  instalação, porque a ISO é um arquivo em disco e não há mídia a remover

### Requirement: Entregar a configuração desatendida ao instalador
O sistema SHALL entregar a configuração desatendida gerada a um local que o
instalador nativo consiga ler durante a sessão live, considerando que a ISO é
montada em loopback e é somente-leitura — nenhum mecanismo SHALL depender de
escrever arquivos dentro da ISO.

#### Scenario: Configuração legível pelo instalador
- **WHEN** o sistema prepara uma instalação desatendida
- **THEN** a configuração é gravada fora da ISO, num local que os parâmetros de
  boot gerados referenciam explicitamente

#### Scenario: Falha na entrega é reportada
- **WHEN** ocorre um erro ao gravar a configuração desatendida
- **THEN** o sistema informa o erro ao usuário e não prossegue silenciosamente
  para um boot que cairia no instalador interativo

### Requirement: Emitir os parâmetros de boot do mecanismo escolhido
O sistema SHALL acrescentar à linha de kernel da entrada de boot os parâmetros
que ativam o modo desatendido do mecanismo daquela distro, e SHALL NOT emitir
os parâmetros de um mecanismo para uma distro que usa outro.

#### Scenario: Parâmetros do subiquity
- **WHEN** a instalação desatendida é preparada para uma distro `Subiquity`
- **THEN** a linha de kernel contém o parâmetro `autoinstall`, antes do
  separador `---`

#### Scenario: Parâmetros do Ubiquity
- **WHEN** a instalação desatendida é preparada para uma distro
  `UbiquityPreseed`
- **THEN** a linha de kernel contém os parâmetros que ativam o Ubiquity
  automático e apontam para o preseed gerado, e não contém `autoinstall`

#### Scenario: Instalação assistida não recebe parâmetros de automação
- **WHEN** o usuário não ativa a instalação automática
- **THEN** a linha de kernel não contém parâmetros de automação de nenhum
  mecanismo, e o boot chega ao instalador nativo interativo

### Requirement: Preservar o caminho já validado do Ubuntu
A introdução de um segundo mecanismo SHALL NOT alterar a configuração
desatendida gerada para builds `Subiquity`. O conteúdo do autoinstall
cloud-init produzido para o Ubuntu SHALL permanecer equivalente ao anterior a
esta mudança.

#### Scenario: Autoinstall do Ubuntu inalterado
- **WHEN** o sistema gera a configuração desatendida para a build de Ubuntu já
  validada, com os mesmos dados de entrada
- **THEN** o autoinstall cloud-init resultante é equivalente ao gerado antes da
  introdução do mecanismo por distro
