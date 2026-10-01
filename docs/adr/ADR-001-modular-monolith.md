# ADR-001 — Modular Monolith

- Status: Accepted
- Date: 2026-10-01
- Scope: Baseline aprovado na TASK 0.0, materializado na TASK 0.1.

## Context

O Vortex Trading Terminal começa como projeto educacional e de portfólio com vários domínios futuros. Ainda não há evidência que justifique serviços distribuídos. Precisamos de separação de responsabilidades, testabilidade e evolução sem antecipar custo operacional.

## Decision

Start as Modular Monolith.

O backend terá implantação inicial como unidade, com Domain, Application, Infrastructure e Api. Os módulos serão organizados internamente com contratos explícitos e propriedade dos dados. Domain permanece independente de frameworks e recursos externos.

## Reasons

- Simplicidade operacional.
- Transações locais quando necessárias.
- Menor custo de desenvolvimento e diagnóstico.
- Facilidade de deploy.
- Fronteiras internas explícitas.
- Possibilidade futura de extração de serviços.

## Consequences

O isolamento inicial por pastas exige disciplina: não acessar tabelas ou entidades internas de outro módulo. Contratos, visibilidade e verificações arquiteturais futuras devem preservar os limites. Não criaremos projetos por módulo antecipadamente.

O backend compartilha recursos e ciclo de deploy; tarefas intensivas poderão precisar de isolamento futuro. A extração de um serviço dependerá de necessidade medida e exigirá avaliar contratos, consistência, dados, operação e migração. Limites internos claros ajudam, mas não tornam a extração gratuita.

## Alternatives

Microservices foram adiados por introduzirem distribuição, coordenação e operação sem benefício demonstrado neste estágio. Um monólito sem limites internos dificultaria a evolução e a testabilidade.
