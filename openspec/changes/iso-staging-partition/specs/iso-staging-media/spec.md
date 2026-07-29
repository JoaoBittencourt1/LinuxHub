## ADDED Requirements

### Requirement: Provisionar uma partição dedicada para a ISO
O sistema SHALL criar, no disco alvo, uma partição dedicada formatada em NTFS,
dimensionada para caber a ISO selecionada com folga, antes de qualquer outra
escrita de preparo da instalação. A partição SHALL ser formatada em um sistema de
arquivos que o ambiente live da distro alvo saiba montar e que não imponha limite
de tamanho de arquivo menor que a ISO.

#### Scenario: Partição criada com filesystem que o live monta
- **WHEN** o sistema prepara uma instalação
- **THEN** existe no disco alvo uma partição formatada em NTFS, com tamanho
  suficiente para a ISO selecionada, e essa partição não é nenhuma das partições
  já existentes do usuário

#### Scenario: FAT32 é recusado para ISO acima de 4 GB
- **WHEN** a ISO selecionada tem mais de 4 GB
- **THEN** o sistema não usa FAT32 na partição de staging, porque o limite de
  tamanho de arquivo do FAT32 impediria a cópia

### Requirement: Copiar a ISO para a partição de staging
O sistema SHALL copiar a ISO selecionada para a partição de staging e SHALL
verificar que a cópia terminou íntegra antes de prosseguir para o boot-staging.
Uma cópia truncada ou interrompida SHALL abortar a instalação com erro explícito,
nunca prosseguir.

#### Scenario: Cópia íntegra libera o boot-staging
- **WHEN** a cópia da ISO para a partição de staging termina
- **THEN** o sistema confere que o arquivo copiado tem o mesmo tamanho do
  original e só então prossegue para configurar o bootloader

#### Scenario: Cópia interrompida aborta a instalação
- **WHEN** a cópia da ISO falha ou termina com tamanho diferente do original
- **THEN** o sistema aborta com mensagem explicando a falha, sem configurar
  bootloader nem registrar entrada de boot

#### Scenario: Progresso é reportado durante a cópia
- **WHEN** a cópia da ISO está em andamento
- **THEN** a interface informa que a cópia está acontecendo, porque ela leva
  tempo perceptível e sem isso a aplicação pareceria travada

### Requirement: Recusar a instalação quando não couber a partição de staging
O sistema SHALL verificar, antes de qualquer escrita em disco, que há espaço
suficiente para a partição de staging. Não havendo, SHALL recusar a instalação
informando quanto espaço é necessário e quanto está disponível.

#### Scenario: Espaço insuficiente recusa antes de escrever
- **WHEN** o disco alvo não tem espaço para acomodar a partição de staging
- **THEN** o sistema recusa a instalação com mensagem quantificando o que falta,
  e o disco permanece exatamente como estava

### Requirement: Recuperar o espaço da partição de staging após a instalação
O sistema SHALL remover a partição de staging depois que a instalação terminar,
devolvendo o espaço ao usuário. A remoção SHALL acontecer somente quando nada mais
depender da partição — nunca durante a sessão live, que lê a ISO dela.

#### Scenario: Staging não é removida durante a sessão live
- **WHEN** o instalador da distro está executando na sessão live
- **THEN** a partição de staging continua intacta, porque a própria sessão live
  depende do arquivo ISO que mora nela

#### Scenario: Espaço devolvido após a instalação
- **WHEN** a instalação termina e o sistema instalado inicia pela primeira vez
- **THEN** a partição de staging é removida e o espaço fica disponível
