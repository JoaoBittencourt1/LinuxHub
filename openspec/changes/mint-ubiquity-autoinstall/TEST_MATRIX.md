# Matriz de teste — mint-ubiquity-autoinstall

## O que está coberto por teste automatizado

224 testes passando. O que eles cobrem, e o que **não** cobrem:

| Área | Coberto | Limite do teste |
|---|---|---|
| Escritor de cpio SVR4 | Sim | Round-trip com um parser escrito no teste, que espelha o algoritmo do kernel (cabeçalho, NUL, alinhamento de 4, trailer). Não prova que **este** kernel monta o initramfs. |
| Alinhamento do cpio | Sim | Que o arquivo sempre termina em múltiplo de 4, para qualquer tamanho de conteúdo — é o que desalinha o segmento seguinte num initramfs concatenado. |
| Equivalência com a referência | Sim | O cpio gerado pelo C# saiu **byte a byte idêntico** ao gerado pelo script Python de validação, que por sua vez foi lido de volta pelo mesmo parser usado para abrir o `initrd.lz` real do Mint. |
| Chaves do preseed | Sim | Que cada chave aparece com o valor certo. Que o **Ubiquity as honra** vem de tê-las lido no `ubiquity_24.04.3+mint19` da ISO, não de teste. |
| Senha nunca em texto claro | Sim | Asserção direta de que o hash entra e a senha não aparece em lugar nenhum do preseed. |
| Perguntas sem valor são omitidas | Sim | Só que a linha não é emitida. Que o instalador então **pergunta** (em vez de pular) é comportamento do debconf, não testado. |
| Fim de linha Unix no preseed | Sim | Que não há `\r`. |
| Resolução do disco (GPT/MBR) | Sim | Só o texto do comando gerado; nenhum `blkid` roda. Mas agora há teste travando que só entram ferramentas conferidas no `initrd.lz` real, e a expressão `sed` foi rodada contra NVMe/SATA/eMMC. |
| Semente ausente do layout | Sim | Que estoura em vez de escolher disco por adivinhação. |
| Receitas partman | Sim | Que dual-boot não declara ESP, que substituir declara em UEFI e não em BIOS, e que swap só aparece quando pedido. **Nenhuma foi aceita por um partman real.** Um teste que afirma a ausência de uma declaração NÃO prova que as partições existentes são preservadas — foi essa confusão que sustentou a falsa confiança antes do incidente. |
| Isolamento entre mecanismos | Sim | Que a entrada do Mint traz `automatic-ubiquity` e nenhum `autoinstall`, e que o cpio entra depois do initrd da ISO. |
| Registry de preparers | Sim | Resolução por mecanismo, e que mecanismo não registrado (ou `None`) estoura do lado do Windows. |
| Não-regressão do Ubuntu | Sim | A suíte inteira de `AutoinstallBuilder`/`AutoinstallStorageBuilder`/`CloudInitSeedWriter` continua verde sem mudança de saída. |
| Confirmações destrutivas ausentes | Sim | Teste dedicado trava que `partman/confirm`, `confirm_nooverwrite` e `confirm_write_new_label` **não** são preseedadas — religá-las passa a ser decisão deliberada. |
| Receita chega ao filesystem live | Sim | Só que o `cp` está no comando. Que ele executa com sucesso no initramfs é o caso 1 da tabela abaixo. |
| Escrita do cpio em disco | **Não** | `UnattendedInitrdWriter` não tem teste: o caminho de staging exige elevação e o de dual-boot escreve no filesystem real. |

## O que precisa de boot real — não validado

Estes são os casos que decidem se a mudança funciona. Nenhum pôde ser executado
durante a implementação.

| # | Cenário | Resultado esperado | Resultado real |
|---|---|---|---|
| 1 | **Gate D1** — boot do Mint com o cpio extra no `initrd` (ver `validation/README.md`) | `/LINUXHUB_PRESEED_OK` existe na sessão live | — |
| 2 | Mint 22.3, dual-boot, automático | Instala inteiro sem nenhuma intervenção, e reinicia sozinho no fim | — |
| 3 | Mint 22.3, substituir, automático | Idem, com a ESP recriada pela receita | — |
| 4 | Ubuntu 24.04.4, automático | Sem regressão: continua instalando como antes desta mudança | — |
| 5 | Mint com o toggle desligado | Boot para no instalador interativo do Mint, sem parâmetro de automação nenhum | — |
| 6 | Mint, disco MBR | O `early_command` resolve o disco por assinatura e o partman acerta o alvo | — |

O caso 1 é pré-requisito dos demais: se ele falhar, a decisão D1 do `design.md`
cai e os casos 2, 3, 5 e 6 não chegam nem a ser executáveis.

## Riscos que nenhum teste desta lista cobre

- **A receita `partman` nunca foi exercitada.** É a parte com maior chance de
  precisar de ajuste no primeiro boot real — a sintaxe é aceita ou rejeitada
  inteira pelo partman, e o erro só aparece lá.
- **A condução do partman sob `automatic-ubiquity` não é determinável por
  leitura estática.** Quem decide é a máquina de estados de debconf do
  `ubi-partman.py` em runtime, e o valor da escolha em `automatically_partition`
  é um identificador `$dev//$id` calculado na hora, não um literal. Só a VM diz.
- **Este documento já falhou uma vez no seu propósito.** O risco do
  `debconf-set` ausente estava escrito aqui, nesta seção, e a implementação foi
  entregue assim mesmo — e foi uma das causas do incidente. Risco anotado não é
  risco resolvido: ou é fechado, ou o que depende dele fica desarmado
  (`constitution.md` §7.1).
