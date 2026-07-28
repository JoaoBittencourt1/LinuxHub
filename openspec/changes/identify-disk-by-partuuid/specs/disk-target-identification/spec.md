## ADDED Requirements

### Requirement: Capturar o PARTUUID da partição semente como identificador do disco alvo (GPT)
Ao criar a partição `CIDATA` num disco de tabela GPT, o sistema SHALL ler o
GUID GPT dessa partição imediatamente após criá-la e disponibilizar esse
valor para a geração do autoinstall, para os dois modos de instalação
(dual-boot e substituir).

#### Scenario: GUID capturado após criação da partição semente
- **WHEN** o sistema cria a partição `CIDATA` num disco de tabela GPT
- **THEN** o sistema lê o GUID GPT dessa partição recém-criada e o mantém
  disponível para a etapa de geração do `storage:` do autoinstall

### Requirement: Capturar a assinatura de disco como identificador do disco alvo (MBR)
Quando o disco alvo usa tabela de partição MBR, o sistema SHALL ler a
assinatura de disco de 4 bytes antes de gerar o autoinstall e disponibilizar
esse valor para a geração do `storage:`, para os dois modos de instalação.

#### Scenario: Assinatura capturada antes da geração do autoinstall
- **WHEN** o sistema prepara o autoinstall para um disco de tabela MBR
- **THEN** o sistema lê a assinatura de disco de 4 bytes desse disco e a
  mantém disponível para a etapa de geração do `storage:`, sem depender da
  criação prévia da partição `CIDATA`

### Requirement: Identificar o disco alvo por dado de tabela de partição, em GPT e em MBR
O sistema SHALL gerar um `storage:` cujo disco final aplicado pelo curtin é
resolvido por um identificador de tabela de partição — PARTUUID em GPT,
assinatura de disco em MBR — não por posição de ranking de tamanho, com
paridade de comportamento entre os dois esquemas.

#### Scenario: Disco identificado corretamente mesmo não sendo o maior nem o menor (GPT)
- **WHEN** a máquina tem três ou mais discos GPT e o disco alvo não é nem o
  maior nem o menor
- **THEN** a instalação aplica o storage no disco correto, identificado pelo
  PARTUUID da partição `CIDATA`, sem exigir que o usuário desconecte discos

#### Scenario: Disco identificado corretamente mesmo não sendo o maior nem o menor (MBR)
- **WHEN** a máquina tem três ou mais discos MBR e o disco alvo não é nem o
  maior nem o menor
- **THEN** a instalação aplica o storage no disco correto, identificado pela
  assinatura de disco, sem exigir que o usuário desconecte discos

#### Scenario: Disco identificado corretamente mesmo com empate de tamanho (GPT)
- **WHEN** dois discos GPT da máquina têm exatamente o mesmo tamanho e um
  deles é o disco alvo
- **THEN** a instalação aplica o storage no disco correto, identificado pelo
  PARTUUID da partição `CIDATA`, sem recusar a instalação por ambiguidade de
  tamanho

#### Scenario: Disco identificado corretamente mesmo com empate de tamanho (MBR)
- **WHEN** dois discos MBR da máquina têm exatamente o mesmo tamanho e um
  deles é o disco alvo
- **THEN** a instalação aplica o storage no disco correto, identificado pela
  assinatura de disco, sem recusar a instalação por ambiguidade de tamanho

#### Scenario: Conjunto de discos muda entre o planejamento e o boot
- **WHEN** um disco adicional (ex.: HD externo) é conectado à máquina depois
  do planejamento no Windows e antes do reboot para instalação, independente
  do disco alvo ser GPT ou MBR
- **THEN** a instalação continua aplicando o storage no disco que contém o
  identificador conhecido (PARTUUID ou assinatura), não no disco que
  passaria a liderar o ranking de tamanho

### Requirement: Resolver o identificador para o disco físico antes da probe de storage
O sistema SHALL gerar um bloco `early-commands` no autoinstall que resolve o
disco físico correspondente ao identificador conhecido (PARTUUID em GPT,
assinatura de disco em MBR) e ajusta o `match:` do storage config antes do
curtin executar a probe de armazenamento.

#### Scenario: early-commands resolve o disco antes do curtin aplicar o layout (GPT)
- **WHEN** o ambiente de instalação Linux boota com um `user-data` gerado
  para um disco GPT
- **THEN** o `early-commands` localiza o disco físico que contém a partição
  com o PARTUUID conhecido e ajusta o `match:` do storage config para
  apontar para esse disco antes de qualquer alteração de storage ser
  aplicada

#### Scenario: early-commands resolve o disco antes do curtin aplicar o layout (MBR)
- **WHEN** o ambiente de instalação Linux boota com um `user-data` gerado
  para um disco MBR
- **THEN** o `early-commands` localiza o disco físico que tem a assinatura
  conhecida e ajusta o `match:` do storage config para apontar para esse
  disco antes de qualquer alteração de storage ser aplicada

### Requirement: Usar o critério de tamanho apenas como último recurso, quando nenhum identificador seguro está disponível
Quando o identificador de tabela de partição não está disponível com
segurança — assinatura de disco MBR igual a `0x00000000`, ou qualquer
identificador duplicado entre discos da máquina — o sistema SHALL usar o
critério de tamanho (`size: largest`/`smallest`) exatamente como hoje, e
continuar recusando a instalação automática quando também esse critério for
ambíguo.

#### Scenario: Assinatura MBR zerada cai no critério de tamanho
- **WHEN** o disco alvo é MBR e sua assinatura de disco é `0x00000000`
- **THEN** o `storage:` gerado usa `match: size: largest` ou
  `match: size: smallest`, conforme aplicável, sem gerar `early-commands`
  baseado em assinatura

#### Scenario: Identificador duplicado entre discos cai no critério de tamanho
- **WHEN** mais de um disco da máquina reporta o mesmo identificador de
  tabela de partição (ex.: discos clonados por imagem)
- **THEN** o sistema não usa esse identificador para o match e recorre ao
  critério de tamanho, sinalizando a ambiguidade

#### Scenario: Nem identificador nem tamanho são seguros
- **WHEN** o disco alvo não tem identificador de tabela de partição seguro
  disponível e também não é nem o maior nem o menor disco da máquina (ou
  empata em tamanho no extremo)
- **THEN** o sistema recusa a instalação automática com uma mensagem
  explicando que não há critério seguro disponível, como acontece hoje
