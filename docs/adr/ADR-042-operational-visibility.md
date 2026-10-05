# ADR-042 — Operational Visibility

- Status: Accepted
- Date: 2026-10-06
- Scope: TASK 8.0. Logs de negócio estruturados e estado operacional exposto.

## Context

Antes desta decisão o sistema tinha logging HTTP (método, path, status, duração) vindo do
`UseHttpLogging` do baseline, e nada mais. Três problemas concretos apareceram ao operar a stack:

1. A execução SQL do EF Core estava em `Information`, então uma única submissão de ordem produziu
   mais de cem linhas de SQL. Os eventos que importam ficavam enterrados, e os logs carregavam nomes
   de parâmetros de query que ninguém precisa ver.
2. Não havia nenhum evento de negócio. Um log mostrava `POST /api/orders 201`, mas não qual ordem,
   qual lado, qual quantidade, nem se ela executou — as perguntas que um operador realmente faz.
3. Não havia como responder "está vivo e funcionando" sem ler o log e o banco. O readiness responde
   sobre o PostgreSQL, não sobre o mercado simulado nem sobre o estado da sessão.

## Decision

**A execução SQL sai do nível Information.** `Microsoft.EntityFrameworkCore.Database.Command` e
`Microsoft.EntityFrameworkCore.Update` passam para `Warning`. O SQL continua disponível elevando o
nível por configuração quando alguém estiver depurando, sem que o padrão carregue ruído.

**Eventos de negócio estruturados no ponto único por onde todos os fatos passam.** O coordinator
emite um evento por fato efetivamente aplicado, com campos nomeados (`Kind`, `OrderId`, `Quantity`,
`Price`, `AppliedAt`) e não texto interpolado, para que uma consulta de log possa agrupar por ordem,
tipo ou preço sem interpretar prosa. Submissão e cancelamento emitem os seus próprios eventos, e uma
submissão que falha emite um aviso com a exceção.

O evento é emitido no `DrainReports` porque **todo** fato normalizado passa por aquele laço; logar em
cada chamador duplicaria a instrumentação e criaria caminhos onde um fato aplicado não é registrado.

**Estado operacional como endpoint, não como log.** `GET /api/status` responde, em todos os
ambientes, o tempo simulado, o último preço, ordens em repouso, total de ordens por situação, número
de fills, quantidade negociada e quantas ordens foram restauradas do banco. É o que uma sondagem ou
um painel externo consulta, e é legível sem parsear log.

**Logging é abstracão, não framework.** `ILogger<T>` é a abstração do `Microsoft.Extensions.Logging`,
já disponível em Application pelo `FrameworkReference` existente, e é opcional na assinatura do
coordinator. Isso significa que os testes de Application continuam construindo o coordinator sem
logger, e nenhum pacote novo entrou.

## Consequences

- Os logs de uma ordem agora contam a história completa em poucas linhas, e o SQL só aparece quando
  pedido.
- Valores exatos aparecem nos logs como o decimal que são, não convertidos, e um campo ausente
  aparece como `(null)` em vez de sumir — a ausência de preço numa aceitação é visível.
- `GET /api/status` é público como o resto da API. Isso é consequência de não haver autenticação
  (ADR-039) e não uma decisão de exposição: quando a autenticação existir, este endpoint deve seguir
  a mesma política das outras rotas.
- **Métricas e tracing distribuído não estão implementados.** Não há contadores Prometheus, histogramas
  nem OpenTelemetry. A decisão foi adiada de propósito: sem um coletor e um destino, instrumentar
  seria custo sem consumidor. O que existe é o estado operacional consultável e os eventos de negócio;
  o passo seguinte é escolher a stack de coleta, e é uma decisão própria.
- Não há alertas. Nada notifica quando o feed para de publicar; alguém precisa consultar o status.

## Alternatives

**Instrumentar OpenTelemetry agora.** Adiada, não rejeitada. Adicionaria pacotes, um exportador e um
destino antes de existir alguém consumindo os dados, e a decisão de stack de observabilidade merece
ser tomada com um alvo em mente.

**Logar dentro de cada chamador do coordinator.** Rejeitada: duplicaria a instrumentação e permitiria
que um caminho novo aplicasse fatos sem registrar nada.

**Enriquecer `/health` com o estado da sessão.** Rejeitada: o readiness responde sobre dependências, e
misturar saúde com estado de negócio faria um mercado parado derrubar o readiness — derrubando o
container por um motivo que não é indisponibilidade.

**Logar o SQL em Information para auditoria.** Rejeitada: auditoria de fatos de negócio pertence aos
eventos estruturados, que têm semântica; SQL é detalhe de implementação e vaza nomes de parâmetros.

## References

- ADR-039 — Single Simulated Session.
- ADR-040 — Durable Execution Scope.
- docs/architecture/backend-foundation.md (pipeline HTTP e logging do baseline).
