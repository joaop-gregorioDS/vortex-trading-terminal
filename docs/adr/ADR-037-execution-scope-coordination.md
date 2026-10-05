# ADR-037 — Execution Scope Coordination

- Status: Accepted
- Date: 2026-10-06
- Scope: TASK 3.0. Dono do escopo de execução sobre o provider do ADR-036 e o workflow do ADR-034.

## Context

O ADR-034 deixou explícito que o **chamador** é dono do escopo: inicia o consumo de relatórios antes
dos comandos, mantém o consumo enquanto comandos estão pendentes, roteia por `OrderId`, fornece
tempos de aplicação e cuida do shutdown. Ele também registrou que "um futuro coordinator pode ser
dono de um consumidor de escopo e despachar `OrderId -> OrderExecutionWorkflow`".

O ADR-036 entregou o provider concreto. Falta exatamente essa peça: alguém que possua o escopo, o
único consumidor lógico e o roteamento — e que escolha os tempos explícitos que o provider exige.

`IExecutionProvider` proíbe relógio autônomo (ADR-033). Um coordinator precisa de um instante
quando o chamador não fornece um, então alguma forma de relógio tem de existir em **algum** lugar.

## Decision

Adicionar `ExecutionCoordinator` em `VortexTrading.Application.Trading.Execution`. Um coordinator
possui um instrumento, um `IExecutionProvider` e o `ChannelReader<ExecutionReport>` desse provider.

**Consumo por drain, não por laço.** Os relatórios são aplicados por `DrainReports(at)`, chamado em
pontos definidos: antes e depois de cada observação e depois de cada comando. O efeito de uma
operação está visível quando ela retorna, o que torna a coordenação determinística e testável. Isso
exige que o stream do provider seja **bufferizado**; um provider cujos relatórios chegam de forma
assíncrona precisaria de um consumidor em execução, o que é decisão separada. Por isso o coordinator
recebe o reader como entrada explícita em vez de alcançar o provider por dentro.

**Relógio apenas na fronteira.** `IApplicationClock` fornece o instante quando o chamador não passa
um. É a única forma de relógio no projeto, vive na fronteira externa e **nunca** é entregue a um
provider. `SystemApplicationClock` implementa o relógio da máquina em Infrastructure; testes
substituem por um relógio controlável, mantendo cenários reprodutíveis.

**Tempo de aplicação é o tempo do fato.** Uma observação produz fatos que pertencem ao instante da
observação, então `Observe` drena com `observation.EventTime`. Comandos drenam com o tempo do
comando. Nenhuma dessas decisões consulta o relógio.

**Porta para observações de mercado.** `IPaperMarketObserver` é uma porta estreita de Application que
o provider de paper implementa. O ADR-033 mantém `IExecutionProvider` livre de tipos de market data,
o que está certo para adapters de corretora que têm preço próprio; um provider de paper é o caso
oposto, e a alternativa seria alargar a fronteira congelada com tipos de mercado. A porta permite ao
coordinator dirigir o provider sem referenciar Infrastructure, preservando a direção de dependências.

**Posse explícita.** O coordinator registra a ordem antes de submeter, para que o roteamento já
conheça o dono quando o provider publicar um fato durante o comando. Um relatório para ordem não
possuída é **erro**, não descarte silencioso: descartar esconderia uma violação de contrato. Se a
submissão falhar, a ordem não fica meio-possuída. Reenviar a mesma ordem é recusado, e o fato de que
uma tentativa já ocorreu é estado **do coordinator**, não do workflow.

**Sem retry, sem reconciliação, sem persistência.** O coordinator não repete comandos, não resolve
incerteza, não grava histórico e não promete exatamente-uma-vez. Ele para onde o ADR-034 parou.

## Guarda de fronteira

O teste do ADR-033 afirmava que `Trading.Execution` contém exatamente seis contratos mais
`OrderExecutionWorkflow`. Ele foi reescrito para exigir uma **lista explícita de tipos autorizados**:
os seis contratos mais `OrderExecutionWorkflow`, `ExecutionCoordinator`, `IApplicationClock` e
`IPaperMarketObserver`, cada um com a decisão que o autoriza. Um tipo público novo passa a falhar o
teste até ser autorizado por nome, em vez de a guarda ser afrouxada por exclusão.

O `OrderExecutionWorkflow` **não** foi alterado. Uma tentativa inicial adicionou duas propriedades
públicas a ele para expor o estado de submissão; o teste de forma pública da TASK 0.42
(`PublicShapeHasOnlyTheThreeOperationsAndNoResourceOrOrderAccessor`) reprovou, corretamente, porque a
superfície do workflow é congelada. O estado passou para o coordinator.

## Consequences

- O escopo de execução passa a ter um dono único e nomeado, com efeitos visíveis ao retorno.
- O consumo por drain torna o comportamento determinístico, mas acopla o coordinator a streams
  bufferizados. Um adapter de corretora real, com stream assíncrono, exigirá um consumidor em
  execução — decisão futura, não um ajuste local.
- Aplicar um lote com instantes misturados pode violar a regra do domínio de que o tempo de aplicação
  não precede a última atualização. O chamador deve passar o instante ao qual o lote pertence.
- O coordinator não é thread-safe e não impede uso concorrente; posse serial continua sendo
  responsabilidade do chamador.
- `IPaperMarketObserver` é uma porta a mais que `IExecutionProvider`. Ela existe para não alargar a
  fronteira congelada, e deve ser reavaliada se um segundo tipo de provider precisar do mesmo papel.

## Alternatives

**Laço de consumo em background com canal de sincronização.** Rejeitada por ora: introduziria
concorrência, coordenação de shutdown e possibilidade de corrida entre o laço e o chamador, antes de
existir um provider que precise disso.

**Colocar o coordinator em Infrastructure.** Rejeitada: ele é caso de uso de aplicação e coordena
regras de Application; em Infrastructure inverteria a direção de dependências.

**Alargar `IExecutionProvider` com tipos de market data.** Rejeitada: quebraria a fronteira congelada
do ADR-033 e acoplaria adapters de corretora a um modelo de preço que não é deles.

**Adicionar propriedades públicas ao workflow.** Rejeitada: a superfície do ADR-034 é congelada, e a
guarda existente existe justamente para impedir esse alargamento incidental.

## References

- ADR-033 — Execution Provider Boundary Contracts.
- ADR-034 — Single-Order Execution Workflow.
- ADR-036 — Deterministic Paper Execution Provider.
- docs/architecture/execution-coordination.md.
