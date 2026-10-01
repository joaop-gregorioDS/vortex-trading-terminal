# Architecture Baseline

Baseline aprovado na TASK 0.0 e materializado estruturalmente na TASK 0.1, em 2026-10-01. Docker Engine com Linux containers foi validado na TASK 0.0-A. Este documento descreve a direção arquitetural; componentes futuros permanecem sem implementação.

## Modular Monolith

Começaremos com um backend implantável como unidade e fronteiras internas explícitas. Microservices somente serão considerados diante de necessidade comprovada de escala, isolamento ou operação independente. O frontend e os serviços de infraestrutura não representam divisão prematura do domínio em microservices.

## Fluxo e dependências

Fluxo conceitual de uma solicitação futura:

```text
Frontend
    ↓ HTTP / real-time
API
    ↓ casos de uso
Application
    ↓ regras
Domain
    ↑ adapters preservam as regras do domínio
Infrastructure
```

O desenho representa responsabilidades, não toda a lista de referências de compilação. A Application define portas para recursos externos; Infrastructure implementa essas portas. O domínio não chama a infraestrutura.

Referências reais aprovadas:

```text
Domain         → nenhuma referência de projeto
Application    → Domain
Infrastructure → Application + Domain
Api            → Application + Infrastructure
```

A referência da API à Infrastructure atende à composição das dependências. Regras de negócio permanecem em Domain e Application. Domain não depende de ASP.NET Core, EF Core, PostgreSQL, Redis, SignalR ou providers externos.

## Organização

| Local | Responsabilidade |
| --- | --- |
| `apps/web` | Frontend futuro; diretório reservado |
| `src/VortexTrading.Api` | Host e interfaces HTTP/real-time futuras |
| `src/VortexTrading.Domain` | Regras e invariantes puras |
| `src/VortexTrading.Application` | Casos de uso, coordenação e portas |
| `src/VortexTrading.Infrastructure` | Adapters de persistência, cache e integrações |
| `tests/backend`, `tests/web` | Testes futuros, sem frameworks adicionados |
| `infra` | Infraestrutura futura, sem Dockerfile ou Compose |
| `docs/architecture`, `docs/adr` | Visão e decisões arquiteturais |

## Módulos futuros

| Módulo | Limite de responsabilidade |
| --- | --- |
| Market Data | Ingestão, normalização, instrumentos e dados de mercado |
| Trading | Ordens, execuções, posições operacionais e Paper Exchange |
| Portfolio | Consolidação de posições, patrimônio, exposição e P&L |
| Analytics | Estatísticas e relatórios |
| Indicators | Implementação central dos indicadores |
| Replay | Reprodução temporal de eventos históricos |
| Backtesting | Simulação determinística e resultados de estratégias |
| Algo Trading | Strategy Engine, Robot Runtime e Strategy Lab |
| Risk | Limites e aprovação de ordens |
| Authentication | Identidade e políticas de acesso |
| Platform | Capacidades operacionais transversais de escopo restrito |

Inicialmente, módulos serão organizados por pastas dentro das camadas quando receberem comportamento. Pastas não garantem isolamento de compilação: contratos explícitos, visibilidade restrita, revisão e futuras verificações arquiteturais sustentarão os limites. Não criaremos projetos por módulo antecipadamente.

Cada módulo será responsável pelos seus dados e invariantes. Interações não acessarão diretamente tabelas ou entidades internas de outro módulo. Portfolio consumirá resultados de Trading. Uma instância PostgreSQL poderá atender ao monólito, com propriedade de dados definida por módulo.

## Direção de evolução

- `IMarketDataProvider`: adapters de simulação, histórico/replay e fornecedor real, com capacidades e origem explícitas.
- `IExecutionProvider`: Paper Execution inicialmente; Broker Execution somente em etapa separada. Trading preserva o ciclo de ordens e Risk aprova sua execução.
- SignalR como recomendação inicial para o navegador; WebSocket direto dependerá de necessidade medida. Snapshot, sequência e recuperação de lacunas serão definidos nos contratos.
- Redis será cache ou distribuição efêmera quando necessário; Pub/Sub não será a única fonte de eventos financeiros recuperáveis.
- Indicators centralizará SMA, EMA, MACD, RSI, Stochastic, Bollinger Bands, ATR, VWAP, ADX e OBV para Charts, Backtesting, Strategies e Robots.
- Relógio e fontes de dados controláveis permitirão replay e backtesting reproduzíveis, evitando uso de dados futuros.
- Vortex Strategy Script seguirá Source → Lexer → Parser → AST → Semantic Validator → Strategy Runtime. Sem `eval`, compilação arbitrária ou comandos de sistema; limites de recursos e isolamento serão requisitos do runtime.
- Logs estruturados, métricas, traces, health checks e OpenTelemetry serão considerados nas fronteiras operacionais em etapas próprias.
- Secrets fora do Git, configuração por ambiente, menor privilégio e containers não-root quando aplicável.
- Dados iniciais simulados ou autorizados; integração e redistribuição de dados reais exigirão condições adequadas do fornecedor.

Nenhum provider, engine, banco, hub, autenticação ou componente de infraestrutura foi implementado nesta etapa.

## Ferramentas e políticas

Todos os projetos têm target `net10.0`. O SDK base é `10.0.401`, com `latestPatch` restrito à feature band `10.0.4xx` e previews desabilitados. Versões novas serão revisadas explicitamente; CI deverá instalar um SDK compatível.

`.gitattributes` determina LF para texto, inclusive shell e PowerShell, independentemente de `core.autocrlf` herdado. Scripts `.bat` e `.cmd` utilizam CRLF. `.editorconfig` orienta UTF-8, espaços e tamanho de indentação por linguagem.
