# ADR-039 — Single Simulated Session

- Status: Accepted
- Date: 2026-10-06
- Scope: TASK 5.0. Composição da API, relógio e alimentação de mercado.

## Context

A API expõe execução, mas três lacunas impediam o app de funcionar de verdade.

O ADR-037 exige um dono único do escopo de execução: um consumidor de relatórios, ordens possuídas
serialmente e nenhuma thread-safety. Uma aplicação web cria um escopo de DI por requisição, o que
produziria um livro de ordens e um consumidor de relatórios por requisição para a **mesma** conta.

O ADR-033 proíbe relógio dentro de um provider e exige tempo explícito. Nada alimentava o provider com
observações, então nenhuma ordem preenchia, e ninguém define "agora" no ambiente simulado.

O ADR-038 derivou posições de fills, mas ninguém as reunia para responder o que o usuário tem.

## Decision

Uma classe, `PaperTradingSession`, é a **única dona** do provider, do coordinator, do relógio e do
estado da sessão, registrada como singleton. Ela expõe apenas operações de sessão; endpoints ficam
finos e não conhecem regra de negócio.

**O tempo é o instante simulado, não o relógio da máquina.** Um relógio interno avança somente pelo
feed; requisições não leem `DateTimeOffset.UtcNow`. Criação, submissão e cancelamento são carimbados
com o instante simulado, o que preserva a regra causal: uma ordem só cruza com observação
estritamente posterior à sua submissão.

**Um único mecanismo de concorrência.** A sessão serializa suas operações com um lock, porque o
coordinator não é thread-safe e o feed roda em timer enquanto um operador envia ordens. Não há
`ConcurrentDictionary`, fila ou ator: um lock torna a serialização explícita e suficiente para uma
sessão.

**O feed não reutiliza os timestamps do gerador.** O gerador de candles de demonstração tem tempos
fixos no passado; publicá-los moveria o relógio simulado para trás, o que o provider recusa
corretamente. O feed avança o instante simulado a partir do início e percorre a série de preços
gerada, mantendo o determinismo sem violar a causalidade.

**Para ao acabar a série.** A série gerada é finita. Ao chegar ao fim, o feed registra e para, em vez
de continuar com preços inventados.

**O portfólio é derivado, nunca armazenado.** Cada leitura reconstrói as posições a partir dos fills
em ordem de execução. Armazenar posições criaria uma segunda cópia de um estado que os fills já
determinam, e as duas poderiam divergir. O custo é percorrer os fills a cada leitura, o que é a troca
correta enquanto a sessão é pequena e em memória.

**A sessão é registrada em todos os ambientes; o chart de demonstração não.** A sessão é o produto,
então existe em Development e em Production. O dataset de candles de demonstração é um fixture fixo e
continua restrito a Development. Consequência aceita: em Production a sessão ainda não grava nem lê
PostgreSQL, então reiniciar o container perde ordens e posições. Resolver isso exige decidir entre
persistir ordens e fills (o modelo do ADR-035 já existe) ou persistir posições; a decisão está
pendente e o deploy não deve ser apresentado como durável.

**O feed é determinístico e finito, em todos os ambientes.** Um feed de produção com dados
autorizados exige decisão sobre fornecedor, permissões e redistribuição, que o projeto não tem. O que
existe é simulação declarada, e ela não deve ser apresentada como dado real.

## Consequences

- O app funciona de ponta a ponta: mercado, ordens, cancelamento e portfólio, em qualquer ambiente.
- O estado é perdido ao reiniciar o processo. O ADR-035 persistiu ordens e fills, mas a sessão ainda
  não grava nem lê o banco; ligar os dois é a pendência mais valiosa do projeto, e é ela que torna o
  deploy durável.
- Sessão única significa uma conta: dois usuários compartilhariam ordens e posições. Um produto
  multiusuário exige chave de conta em toda a fronteira, decisão não tomada aqui.
- O `lock` serializa; com carga real ele viraria contenção, e um ator ou fila seria necessário.
- `cash` é uma constante de sessão, não um saldo contábil: o domínio não modela ledger (ADR-038).
- O feed para quando a série gerada termina. Um app hospedado deixa de receber preços depois disso
  até o processo reiniciar, o que é aceitável para uma demonstração e inaceitável para uso contínuo.

## Alternatives

**Sessão por requisição.** Rejeitada: quebra o dono único do escopo, cria um consumidor de relatórios
por requisição e perde ordens em repouso.

**Persistir posições e lê-las.** Adiada, não rejeitada: é a resposta correta na escala em que a
reconstrução ficar cara, e exige decidir como o custo é gravado.

**Relógio da máquina no provider.** Rejeitada pelo ADR-033: destruiria a reprodutibilidade e a
possibilidade de replay histórico.

**Feed em produção sem decisão de dados.** Rejeitada: publicar preços simulados como se fossem reais
contraria o escopo declarado do projeto.

## References

- ADR-033 — Execution Provider Boundary Contracts.
- ADR-036 — Deterministic Paper Execution Provider.
- ADR-037 — Execution Scope Coordination.
- ADR-038 — Average-Cost Position Accounting.
