## MODIFIED Requirements

### Requirement: Particionar o disco conforme o modo de instalação
No modo substituir, o sistema SHALL liberar as partições existentes do disco alvo e
criar a partição Linux no espaço resultante, **preservando a partição de staging**
que hospeda a ISO em uso. O modo substituir SHALL NOT delegar a um layout que
reescreva o disco inteiro: enquanto a sessão live estiver rodando, a partição de
staging não pode ser liberada, e pedir a liberação do disco inteiro faz o instalador
abortar ao tentar soltar um dispositivo que ele próprio está usando. No modo
dual-boot, o sistema SHALL criar a partição Linux dentro do espaço não alocado
deixado pelo shrink executado no lado Windows, sem alterar as partições existentes,
e SHALL igualmente preservar a partição de staging.

#### Scenario: Replace libera o Windows preservando a staging
- **WHEN** o modo de instalação é substituir
- **THEN** as partições do Windows são liberadas e a partição Linux é criada nesse
  espaço, enquanto a partição de staging permanece intacta e legível durante toda a
  instalação

#### Scenario: Replace não pede liberação do disco inteiro
- **WHEN** o sistema gera a configuração de particionamento para o modo substituir
- **THEN** a configuração declara explicitamente o que preservar e o que liberar, em
  vez de pedir um reparticionamento total do disco

#### Scenario: Dual-boot preserva partições existentes
- **WHEN** o modo de instalação é dual-boot
- **THEN** o sistema cria a partição Linux apenas no espaço não alocado deixado pelo
  shrink, sem apagar ou redimensionar qualquer partição existente

#### Scenario: Instalação conclui com a ISO no mesmo disco do alvo
- **WHEN** a ISO usada para bootar está numa partição do próprio disco que está sendo
  instalado
- **THEN** a instalação conclui sem erro de liberação de dispositivo, porque a
  partição que hospeda a ISO é declarada como preservada
