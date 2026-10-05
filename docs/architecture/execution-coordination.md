# Execution Coordination — TASK 3.0

Dono do escopo de execução, sobre o checkpoint `a25890d`. Liga o provider determinístico do ADR-036 ao
workflow do ADR-034 e fecha o papel que o ADR-034 atribuía ao chamador.

## Composição

```text
ExecutionCoordinator
  ├── InstrumentId                     um instrumento por escopo
  ├── IExecutionProvider               o provider do escopo
  ├── IApplicationClock                instante quando o chamador não fornece um
  └── ChannelReader<ExecutionReport>   o stream do provider, consumido só por ele
```

O coordinator recebe o reader como entrada explícita. Ele não alcança o provider por dentro, o que
mantém Application independente de Infrastructure e deixa claro quem consome o stream.

## Operações

| Operação | Efeito |
| --- | --- |
| `SubmitOrderAsync(order, submittedAt?)` | Cria o workflow, registra a ordem e submete; drena em seguida |
| `RequestCancellationAsync(orderId, requestedAt?)` | Drena, pede cancelamento pelo workflow, drena de novo |
| `Observe(observation)` | Drena, entrega a observação ao provider, drena com o tempo do evento |
| `DrainReports(at)` | Aplica todo fato bufferizado no instante informado; retorna quantos aplicou |
| `Owned(orderId)` | Resolve o workflow dono, ou falha |

Chamadas sem instante explícito usam `IApplicationClock`. Chamadas com instante explícito não
consultam relógio nenhum.

## Consumo e visibilidade

O coordinator é o **único consumidor lógico** do stream. Ele drena em pontos definidos, sempre com um
instante explícito:

- após um comando, com o tempo do comando;
- antes e depois de uma observação, com o tempo do evento da observação.

Consequência prática: quando `SubmitOrderAsync` retorna, o `Accepted` já foi aplicado à ordem.
Quando `Observe` retorna, os fills daquela observação já foram aplicados. Não há espera, polling nem
corrida entre um laço de fundo e o chamador.

Isso exige stream **bufferizado**. O provider de paper usa um canal ilimitado com leitor único. Um
provider com stream assíncrono de verdade precisará de um consumidor em execução — decisão separada,
registrada como consequência no ADR-037.

## Regras de posse

- A ordem é registrada **antes** da submissão, para que o roteamento conheça o dono quando o provider
  publicar um fato durante o comando.
- Um relatório para ordem não possuída **falha**. Descartar esconderia violação de contrato.
- Se a submissão falhar, a ordem é removida do escopo e não fica meio-possuída. O fato de que uma
  tentativa ocorreu permanece, e o escopo recusa reenvio da mesma ordem.
- Ordem ou observação de outro instrumento é recusada.

Esse estado de "tentativa já feita" vive no coordinator, não no workflow. Expor isso pelo workflow
exigiria alargar a superfície pública congelada do ADR-034, o que o teste de forma pública da
TASK 0.42 impede — corretamente.

## Relógio

`IApplicationClock` existe porque o coordinator precisa de um instante quando o chamador não fornece
um, e o ADR-033 proíbe relógio dentro de um provider. O relógio é lido **somente** na fronteira
externa:

- `SystemApplicationClock` (Infrastructure) lê a máquina;
- testes usam um relógio fixo, mantendo cenários reprodutíveis;
- nenhum provider recebe um relógio.

## Limites

- Sem retry automático, reconciliação de resultado incerto, histórico durável ou exatamente-uma-vez.
- Sem persistência do escopo: reiniciar o processo perde o livro e o estado de submissão.
- Não thread-safe: posse serial é responsabilidade do chamador.
- Aplicar um lote com instantes misturados pode violar a regra do domínio de que o tempo de aplicação
  não precede a última atualização. Passe o instante ao qual o lote pertence.
- Um instrumento por escopo.

## Validação

9 testes em `tests/backend/VortexTrading.Infrastructure.Tests/Trading/Coordination` cobrem: efeitos
aplicados antes do retorno do comando, roteamento de relatórios para a ordem dona sem cruzar ordens,
cancelamento até `Cancelled`, cancelamento de ordem desconhecida falhando sem inventar estado,
reenvio da mesma ordem recusado, ordem e observação de outro instrumento recusadas, relógio
fornecendo o tempo quando o chamador omite, `DrainReports` idempotente com stream vazio e
observações repetidas do mesmo instante sem regredir o tempo de aplicação.

## Referências

- ADR-037 — Execution Scope Coordination.
- ADR-034 — Single-Order Execution Workflow.
- ADR-036 — Deterministic Paper Execution Provider.
- [paper-execution-provider.md](paper-execution-provider.md).
