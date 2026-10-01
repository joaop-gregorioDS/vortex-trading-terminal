# Vortex Trading Terminal

> An educational and portfolio-oriented financial trading platform developed by Vortex Software.

Projeto de terminal financeiro web original para estudar engenharia de software, dados de mercado e simulação de estratégias. O desenvolvimento seguirá etapas revisáveis, com responsabilidade clara sobre cálculos e execução.

## Estágio atual

Baseline de engenharia da TASK 0.1: repositório, políticas de arquivos, solution .NET e quatro projetos estruturais. A API contém apenas o host mínimo, sem endpoints. Frontend, testes, persistência e infraestrutura ainda não foram implementados.

## Arquitetura e stack planejada

- Modular Monolith com limites internos explícitos e dependências orientadas ao domínio.
- Frontend: Next.js, React e TypeScript, com bootstrap em tarefa própria.
- Backend: C# e ASP.NET Core sobre .NET 10 LTS.
- Persistência futura: PostgreSQL, EF Core e Npgsql.
- Real-time e cache futuros: SignalR e Redis conforme necessidade.
- Infraestrutura futura: containers Linux, Docker Compose e VPS com Nginx.

Consulte a [visão arquitetural](docs/architecture/overview.md) e os [ADRs](docs/adr/).

## Validação do baseline

Pré-requisito: SDK .NET `10.0.401` ou patch estável posterior da mesma faixa `10.0.4xx`, conforme `global.json`.

```sh
dotnet restore VortexTrading.slnx
dotnet build VortexTrading.slnx --no-restore
```

`rollForward: latestPatch` permite correções de patch na mesma feature band sem avançar para outra faixa, versão minor ou major. Previews estão desabilitados. Desenvolvimento e CI devem usar essa política; para reprodução estrita, ambos devem selecionar o mesmo patch instalado.

## Princípios

- Valores financeiros exatos usam precisão decimal e regras explícitas de arredondamento.
- Dados e execução serão desacoplados dos providers externos.
- Indicadores terão uma implementação central reutilizável.
- Estratégias de usuários não executarão código arbitrário ou comandos do sistema.
- Secrets ficam fora do Git; `.env.example` é permitido somente com placeholders.
- Dependências e versões serão revisadas por etapa.

## Roadmap macro

1. Baseline de engenharia e arquitetura.
2. Bootstrap revisado do frontend, testes e infraestrutura de desenvolvimento.
3. Dados simulados/autorizados, visualização e Paper Trading.
4. Portfolio, Risk e indicadores centrais.
5. Replay, backtesting, Strategy Lab e runtime de estratégias.
6. Observabilidade, automação de validação e deploy.

Essas etapas descrevem intenção futura, sem funcionalidades entregues neste baseline.

## Escopo e referências

O projeto não é um produto de corretagem e não oferece recomendação de investimento. A execução inicial será exclusivamente Paper Trading. Os dados iniciais serão simulados ou devidamente autorizados; dados reais dependerão de permissões e condições do fornecedor.

Não há afiliação com Nelogica, Profit ou outras plataformas usadas como referências conceituais. Código, marca, textos, identidade visual, layouts proprietários e ativos de terceiros não serão copiados.
