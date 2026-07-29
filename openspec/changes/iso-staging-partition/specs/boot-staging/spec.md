## MODIFIED Requirements

### Requirement: Preparar a ISO como arquivo acessível ao bootloader de staging
O sistema SHALL garantir que a ISO da distro selecionada esteja disponível como um
arquivo numa partição dedicada de staging (ver `iso-staging-media`) que o bootloader
de staging consiga ler no momento do boot, sem exigir um pendrive USB. A ISO SHALL
ser lida dessa partição, e não do volume que hospeda o Windows, por duas razões: o
volume do Windows pode estar criptografado com BitLocker — que o bootloader não sabe
ler — e no modo substituir esse volume precisa ser liberado pelo instalador da
distro, o que é impossível enquanto ele hospeda a ISO em uso.

#### Scenario: ISO já baixada é copiada para a partição de staging
- **WHEN** o usuário já baixou a ISO via `install-wizard` para o caminho padrão de
  downloads do LinuxHub
- **THEN** o sistema de boot-staging usa a cópia dessa ISO na partição de staging
  como origem do boot, e o bootloader localiza o arquivo lá

#### Scenario: Volume do Windows criptografado não impede o boot
- **WHEN** o volume que hospeda o Windows está protegido por BitLocker
- **THEN** o bootloader de staging ainda localiza e inicia a ISO, porque ela está na
  partição de staging, que não é criptografada

### Requirement: Bootar a ISO da distro via loopback
O sistema SHALL configurar o bootloader de staging para inicializar o kernel e
initrd contidos na ISO da distro diretamente via loopback, passando os parâmetros de
linha de comando específicos exigidos pela distro alvo para reconhecer que está
sendo iniciada a partir de um arquivo ISO em disco (não de mídia removível). O
caminho da ISO configurado SHALL apontar para a cópia na partição de staging.

#### Scenario: ISO do Ubuntu inicia em ambiente live
- **WHEN** o usuário seleciona a entrada de boot de staging e a distro alvo é Ubuntu
- **THEN** o ambiente live do Ubuntu (casper) inicia normalmente a partir do arquivo
  ISO em disco, sem exigir mídia USB

#### Scenario: Partição montada pelo live é a de staging
- **WHEN** o ambiente live monta a partição que hospeda a ISO para lê-la
- **THEN** a partição montada é a de staging, e as partições do usuário permanecem
  não montadas pela sessão live
