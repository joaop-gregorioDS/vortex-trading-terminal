# ADR-040 — Durable Execution Scope

- Status: Accepted
- Date: 2026-10-06
- Scope: TASK 6.0. Durabilidade das ordens do escopo de execução.

## Context

O ADR-039 registrou como consequência que reiniciar o processo perde ordens e posições, e o ADR-035
já tinha o modelo de persistência do agregado `Order` pronto e migrado. O que faltava era a ligação:
nada gravava, e nada era lido de volta.

Três restrições definiam a solução. O escopo de execução é um singleton e vive mais que qualquer
requisição, então não pode depender de um `DbContext` scoped. Application não pode conhecer EF nem
Npgsql (ADR-003). E a posição não deve ser armazenada: ela é derivada dos fills (ADR-038), e uma
cópia poderia divergir das execuções que a determinam.

## Decision

**Porta em Application, adapter em Infrastructure.** `IOrderStore`, em `Trading.Execution`, com duas
operações: salvar as ordens fornecidas e carregar todas. Application continua sem EF; o
`PostgresOrderStore` implementa a porta em Infrastructure.

**Salvar é substituição em bloco, não delta.** O contrato recebe as ordens e grava cada uma com seus
fills, inserindo ou atualizando. O agregado carrega os fills como parte de si, e um contrato de delta
exigiria que o adapter conhecesse o que mudou — exatamente o tipo de suposição que produz bugs de
rastreamento. O custo é reescrever linhas que não mudaram.

**Gravado em pontos de mutação, e só quando houve mutação.** O coordinator persiste depois de
submeter, depois de cancelar e depois de uma observação **que aplicou algum fato**. Observações
chegam a cada segundo e a maioria não preenche nada; gravar sempre reescreveria o livro inteiro por
tick sem que nada tenha mudado. Os testes cobrem os dois lados: nada gravado quando nada mudou, e
gravado quando um fill ocorreu.

**Fills são inseridos individualmente; o agregado é atualizado campo a campo.** Atribuir a instância
recebida à entrada rastreada não é possível: o domínio não expõe setters, então o EF não tem como
copiar os valores, e destacar o grafo rastreado para anexar outro arriscaria a falha de chave
duplicada que essa lógica existe para evitar. Lifecycle, `LastUpdatedAt` e motivo de rejeição são
escritos explicitamente; fills desconhecidos são inseridos.

**Um registro de options, não dois.** `AddDbContext` e `AddDbContextFactory` juntos registram dois
`DbContextOptions<T>`, e o factory singleton não pode consumir o scoped. O host **recusava iniciar**
com essa combinação. Agora só o factory registra options, e o contexto scoped é derivado dele, o que
preserva consumidores existentes e mantém uma única fonte de configuração.

**Recuperação é preguiçosa e explícita, e nunca fatal.** Carregar no construtor tornaria uma queda de
banco um processo que não sobe — o oposto do que a fundação de persistência definiu. A carga ocorre na
primeira operação que precisa dela, e falha vira aviso: o app continua em memória e o readiness segue
reportando o problema real. Toda leitura de estado possuído chama `EnsureRecovered()` de forma
explícita.

**Ordem restaurada não recebe workflow.** O construtor do workflow exige `Created`, e fabricar
histórico de comandos para uma ordem parcialmente preenchida afirmaria tentativas que não
aconteceram. Uma ordem restaurada aparece no livro e no cálculo de posição, mas não pode ser
comandada. Essa lacuna é deliberada e está registrada: recuperar execução em voo é uma decisão
própria, com reconciliação, e não deve ser resolvida por conveniência.

**Posições continuam derivadas.** Nada de posição é gravado. A leitura reconstrói a partir dos fills
persistidos, em ordem de execução.

## Consequences

- Ordens e posições sobrevivem ao restart do processo. Verificado por HTTP: matar a API e subir de
  novo devolve a mesma ordem, com o mesmo status e os mesmos fills, e a mesma posição, com quantidade,
  custo e média idênticos.
- Cada gravação reescreve todas as ordens do escopo. Com muitas ordens isso vira custo real, e um
  contrato de delta ou uma projeção de posição será a resposta na escala em que doer.
- Uma ordem viva no momento da queda volta sem poder ser comandada. Enquanto isso não for decidido, o
  operador não tem como cancelá-la pela API.
- O `cash` continua sendo uma constante de sessão, não um saldo contábil; um ledger é decisão
  separada.
- Testes de persistência passaram a usar um instrumento reservado e apagam apenas os próprios dados.
  A versão anterior apagava a tabela inteira e destruía dados de uma API em execução no mesmo banco de
  desenvolvimento — um defeito de teste que mascarou a recuperação por várias rodadas.

## Alternatives

**Persistir posições.** Rejeitada: cria uma segunda cópia de um estado que os fills já determinam,
com risco de divergência (ADR-038).

**Gravar a cada tick.** Rejeitada: amplificação de escrita sem mudança de estado.

**Carregar no construtor.** Rejeitada: torna indisponibilidade de banco uma falha de boot.

**Repository genérico com Unit of Work.** Rejeitada: a fundação de persistência já havia decidido não
introduzir abstrações genéricas; a necessidade é concreta e estreita.

## References

- ADR-003 — PostgreSQL + EF Core + Npgsql.
- ADR-035 — Order Aggregate Persistence.
- ADR-038 — Average-Cost Position Accounting.
- ADR-039 — Single Simulated Session.
