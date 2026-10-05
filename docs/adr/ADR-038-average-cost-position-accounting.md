# ADR-038 — Average-Cost Position Accounting

- Status: Accepted
- Date: 2026-10-06
- Scope: TASK 4.0. Módulo Portfolio, posições por instrumento.

## Context

Com execução funcionando, o produto precisa responder o que o usuário tem: quantidade, custo, P&L.
Nenhum método contábil havia sido escolhido, e a escolha muda o resultado numérico que aparece na
tela e no relatório.

O ADR-002 exige exatidão em valores financeiros e proíbe escala universal. O ADR-035 mostrou que
`decimal` sozinho não basta: multiplicar quantidade por preço estoura ou arredonda em silêncio, o que
é inaceitável para custo e resultado.

O `OrderFill` não carrega instrumento — ele pertence a uma ordem, que conhece o instrumento. Qualquer
cálculo de posição precisa que o chamador resolva esse contexto.

## Decision

Posição por instrumento com **custo médio** como método contábil, em
`VortexTrading.Domain.Portfolio.Position`, imutável: cada fill produz uma posição nova.

**Custo total é a autoridade; a média é derivada.** Comprar soma quantidade e soma custo; vender
realiza `(preço − custo médio) × quantidade vendida` e mantém a mesma média para o restante, que é a
propriedade que define o método. Guardar a média como estado permitiria divergência entre média e
custo; derivá-la elimina essa classe de erro.

**Toda derivação passa por `ExactDecimal`.** Custo, média, resultado realizado e não realizado usam
aritmética inteira exata (ADR-035), nunca `decimal` cru. Um resultado que não caiba em `decimal` é
rejeitado com `DomainException` em vez de transbordar ou arredondar sem aviso.

**Escala monetária é política explícita por instrumento**, com padrão de 8 casas. O ADR-002 proíbe
assumir escala universal, então ela é um parâmetro do chamador, não uma constante escondida na
aritmética. A média é calculada com duas casas extras para reduzir erro de arredondamento e limitada
ao máximo representável, porque uma posição com escala 27 não tem espaço para duas casas a mais.

**Fechar a posição zera o custo.** Se a quantidade restante chega a zero, o custo residual é zerado
por decisão explícita. Sem isso, arredondar a média deixaria um resíduo de custo em uma posição sem
unidades, que apareceria como P&L fantasma.

**Venda nunca torna a posição negativa.** Se a venda excede o que existe, o excedente é **ignorado e
reportado** em `PositionFillResult`, em vez de criar posição vendida. Venda descoberta é política de
trading que este tipo não pode assumir; o chamador decide o que fazer com o excedente.

**Instrumento é resolvido pelo chamador.** `Apply` recebe o fill e o lado, sem tentar inferir o
instrumento de um `OrderFill` que não o carrega. Inventar essa resolução aqui duplicaria uma
responsabilidade que pertence a quem conhece a ordem.

## Consequences

- O resultado exibido é auditável: custo total explica a média, e a média explica o realizado.
- Ordens parcialmente preenchidas são suportadas naturalmente; cada fill é aplicado isoladamente.
- O método de custo médio difere de FIFO e de custo específico. Uma futura comparação fiscal ou
  relatório que exija FIFO precisará de outra estrutura de dados, não de um ajuste neste tipo.
- O excedente de venda precisa ser tratado pelo chamador; ignorá-lo silenciosamente na camada
  superior reproduziria o problema que o `PositionFillResult` existe para evitar.
- Posições não são persistidas nesta fase. Persistir exigirá decidir se o custo é gravado ou
  reconstruído a partir dos fills; reconstruir é mais lento e mais correto, gravar é mais rápido e
  cria um segundo estado a manter consistente.
- Não há agregação de portfólio, exposição, alocação nem conversão de moeda nesta decisão.

## Alternatives

**FIFO ou custo específico.** Rejeitada por ora: exige manter a lista de lotes abertos e escolher qual
lote cada venda consome, o que é mais estado e mais regra sem necessidade demonstrada. Custo médio é
o padrão mais simples que ainda é defensável e auditável.

**Guardar a média como campo persistido.** Rejeitada: cria duas fontes de verdade para o mesmo fato e
permite divergência silenciosa após uma série de arredondamentos.

**Criar posição vendida quando a venda excede.** Rejeitada: venda descoberta envolve margem, risco e
regras de mercado que não foram decididas, e o ADR-001 mantém Risk fora desta fase.

**Usar `decimal` cru com arredondamento em cada passo.** Rejeitada pelo ADR-002 e comprovadamente
incorreta: multiplicar preço por quantidade pode estourar, e acumular arredondamento desloca o custo.

## References

- ADR-002 — Financial Numeric Types.
- ADR-001 — Modular Monolith (módulo Portfolio).
- ADR-035 — Order Aggregate Persistence.
- `src/VortexTrading.Domain/Common/ExactDecimal.cs`.
