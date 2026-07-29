# Matriz de teste — iso-staging-partition

## O que está coberto por teste automatizado

158 testes passando. O que eles cobrem, e o que **não** cobrem:

| Área | Coberto | Limite do teste |
|---|---|---|
| Montagem dos scripts PowerShell | Sim | Só o texto gerado. Nenhum roda de verdade — exige elevação/UAC. |
| Crase dupla em string verbatim | Sim | Asserção direta em todos os scripts (bug real do `BootSecurityService`). |
| Chaves balanceadas nos scripts | Sim | Heurística; não substitui o parser do PowerShell. |
| Storage config do modo substituir | Sim | Que o YAML **não** contém `layout:`, que staging/semente aparecem e que o Windows é omitido. Não valida que o curtin aceita o YAML. |
| Script de limpeza pós-instalação | Sim | Sintaxe conferida com `sh -n` real, além das asserções de defesa (PARTUUID, rótulo+fs, `/proc/mounts`, auto-desabilitação). |
| Pré-checagem de espaço | Sim | Que recusa antes de criar `PendingConfirmation`. |
| Guardas de Secure Boot / BitLocker | Sim | Só o caminho positivo de recusa. O caso **BitLocker ligado** nunca foi exercitado. |

## O que precisa de VM — não validado

Estes são os casos que decidem se a mudança funciona. Nenhum pôde ser executado
durante a implementação.

| # | Cenário | Resultado esperado | Resultado real |
|---|---|---|---|
| 1 | Substituir, disco cheio de Windows, autoinstall ligado | Instalação conclui. **Sem** `clear-holders: FAIL` — é o erro que originou toda esta mudança | — |
| 2 | Dual-boot com BitLocker ligado no C: | Wizard **recusa** com o passo a passo do `manage-bde` (design D6) | — |
| 3 | Dual-boot, BitLocker desligado | ISO boota a partir da staging; o C: **não** aparece montado em `/isodevice` na sessão live | — |
| 4 | Primeiro boot após instalar | Staging e semente sumiram; nenhuma outra partição foi tocada | — |
| 5 | Primeiro boot com rótulo divergente (adulterar antes de reiniciar) | Script **não** apaga nada e se desabilita mesmo assim | — |
| 6 | Disco sem espaço para a staging | Recusa antes de qualquer escrita; disco intacto | — |
| 7 | Secure Boot ligado | Wizard recusa antes de encostar no disco | — |
| 8 | Cópia da ISO interrompida (matar o processo elevado) | Aborta com erro; nenhum bootloader registrado | — |

## Ordem sugerida

1. **Caso 1 primeiro** — é a razão de existir da mudança. Se falhar, o resto não importa.
2. Casos 3 e 4, que fecham o ciclo completo do caminho feliz.
3. Caso 5 antes de confiar a limpeza a qualquer usuário real: é código que apaga
   partição na máquina já instalada.
4. Casos 2, 6, 7, 8 (recusas) por último — falham "para o lado seguro".

## Nota sobre a VM de teste

A VM usada em 2026-07-29 ficou inutilizável: o `clear-holders` apagou a ESP antes de
falhar. Recriar do zero, e deixar Secure Boot e vTPM **desligados** nas configurações do
Hyper-V, exceto nos casos 2 e 7, que existem justamente para exercitá-los.
