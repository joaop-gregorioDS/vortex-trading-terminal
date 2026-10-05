# ADR-041 — Same-Origin Trading Panel

- Status: Accepted
- Date: 2026-10-06
- Scope: TASK 7.0. Frontend de operação sobre a API de trading.

## Context

O backend estava completo e durável, mas o app web só mostrava o gráfico de demonstração: um
terminal financeiro que ninguém conseguia operar pela tela.

Três restrições definiam a solução. O navegador precisa alcançar a API tanto em desenvolvimento
quanto atrás do Nginx em produção, sem manter duas configurações divergentes. Nenhum valor exato pode
ser corrompido na exibição: o backend entrega decimais como string justamente para não perder
precisão, e o frontend não pode reintroduzir isso. E o app é declaradamente uma simulação, o que
precisa ser visível e não apenas documentado.

## Decision

**Uma origem, nos dois ambientes.** O cliente chama sempre `/api/...` na mesma origem. Em produção o
Nginx encaminha esse prefixo para o container da API; em desenvolvimento o `next.config.ts` faz o
mesmo por rewrite. Consequências: nenhuma política de CORS para manter, nenhuma variável de build
usada pelo navegador, e nenhuma chance de o dev e a produção divergirem no caminho da API. Em
produção, sem `VORTEX_API_BASE_URL`, o rewrite fica vazio de propósito, porque o Nginx é dono do
prefixo.

**O cliente não faz aritmética de dinheiro.** Valores chegam como string decimal e são apenas
agrupados e aparados para leitura; comparações de preço são feitas dígito a dígito, sem conversão para
ponto flutuante. Quando uma ordem tem fills a preços diferentes, a célula mostra o **intervalo** em
vez de calcular uma média ponderada, porque média ponderada é aritmética de dinheiro e pertence ao
backend, cuja exatidão garante o número. Um teste cobre o caso de 27 casas decimais sobrevivendo à
exibição.

**Falha de leitura é estado visível, não painel vazio.** O transporte devolve resultado
discriminado em vez de lançar, e a interface mostra "a API não respondeu a tempo" ou o título do
Problem Details. Um operador que não vê o preço precisa saber que não vê, e não confundir ausência de
dado com mercado parado.

**Polling, não streaming.** Preço e ordens são relidos a cada 1,5 s. O portfólio só é relido quando a
assinatura de fills muda, porque fill é a única coisa que altera posição. SignalR é a evolução
prevista para tempo real e não foi antecipado: o protocolo de snapshot, sequência e recuperação de
lacunas exigiria decisão própria, e polling sobre uma sessão simulada não tem o problema que ele
resolve.

**A simulação é rotulada na tela.** O cabeçalho diz "Paper Trading · dados simulados", o cartão de
mercado traz a origem determinística e o rodapé repete o escopo. Rótulo visível é parte de não
apresentar dado simulado como real.

## Consequences

- O app é operável de ponta a ponta pela tela: enviar ordem, acompanhar execução, cancelar ordem viva
  e ler posição e resultado.
- O polling de 1,5 s é aceitável para uma sessão simulada e um operador; com muitos clientes ou dados
  reais, streaming passa a ser necessário.
- O intervalo de preços em vez da média ponderada é menos conveniente de ler e mais correto: não
  introduz cálculo financeiro no cliente.
- A lista mostra a sessão inteira, sem paginação nem filtro. Com muitas ordens isso deixa de servir.
- Não há autenticação: qualquer visitante opera a mesma conta simulada, o que o ADR-039 já registrava.
- O rewrite do Next só existe quando um endereço é conhecido em tempo de build. Uma implantação que
  sirva o frontend sem Nginx e sem essa variável não alcança a API, e isso é uma falha explícita, não
  silenciosa.

## Alternatives

**Base URL absoluta vinda de variável `NEXT_PUBLIC_`.** Rejeitada: duplica a configuração de caminho
entre dev e produção, exige CORS e torna o build dependente do ambiente.

**Calcular média ponderada no cliente.** Rejeitada: aritmética financeira no frontend contraria o
ADR-002, e o backend já tem aritmética exata para isso.

**Streaming com SignalR agora.** Adiada, não rejeitada: é a direção prevista na visão arquitetural e
exige definir snapshot, sequência e recuperação de lacunas antes de valer a pena.

**Server Component buscando as ordens.** Rejeitada: a mesa precisa reagir a envio, cancelamento e
execução sem recarregar a página.

## References

- ADR-002 — Financial Numeric Types.
- ADR-039 — Single Simulated Session.
- ADR-040 — Durable Execution Scope.
- docs/architecture/production-deploy.md.
