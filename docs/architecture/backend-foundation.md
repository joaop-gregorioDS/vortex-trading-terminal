# Backend Foundation — TASK 0.3

Fundação técnica sobre o checkpoint `c51aeaf`, sem domínio financeiro, persistência, autenticação ou comunicação real-time.

## Composição e dependências

`Program.cs` é a Composition Root. Chama `AddApplication()` e `AddInfrastructure(configuration)`; essas extensões validam argumentos e ainda não registram casos de uso ou adapters. Não há implementações fictícias.

Referências de projeto preservadas:

```text
Domain         → nenhuma
Application    → Domain
Infrastructure → Application + Domain
Api            → Application + Infrastructure
```

Application e Infrastructure usam `FrameworkReference` para `Microsoft.AspNetCore.App` a fim de acessar as abstrações oficiais de DI/configuração sem adicionar NuGet. Isso vincula essas duas bibliotecas ao shared framework ASP.NET Core, embora não introduza código HTTP nelas. A API já utiliza esse framework implicitamente pelo Web SDK. O Domain continua sem referências de framework ou pacotes adicionais.

## Configuração

`VortexTradingOptions` pertence à API e utiliza a seção `VortexTrading`. Sua única propriedade é `ApplicationName`. O Options Pattern vincula a configuração, rejeita valor ausente, vazio ou whitespace e utiliza `ValidateOnStart`.

O nome é resolvido de `IOptions<VortexTradingOptions>` na inicialização e registrado como metadado não sensível. Configuração inválida impede o processo de começar a atender HTTP. Uma variável de ambiente de processo como `VortexTrading__ApplicationName` pode substituir o valor, sem criar `.env` ou endpoint de configuração.

## Pipeline HTTP

- `AddProblemDetails` registra a representação padrão de erros. A personalização remove `detail` e inclui somente um identificador de correlação `traceId`.
- `UseExceptionHandler` é aplicado em todos os ambientes, sem endpoint de erro artificial ou middleware próprio. `SuppressDiagnosticsCallback` retorna `false` para preservar diagnósticos no servidor sob .NET 10.
- `UseStatusCodePages` permite Problem Details para erros sem corpo, incluindo 404, com negociação de conteúdo do mecanismo oficial.
- `UseHttpLogging` envolve o tratamento de erros para observar o status final. Registra somente método, path, status e duração, em um evento combinado. Headers, cookies, query strings e bodies não estão habilitados.
- O nível específico de HttpLogging é Information; o restante de ASP.NET Core permanece em Warning para evitar logs informativos de hosting que possam incluir query strings.

Somente `GET /health` é mapeado, com Health Checks nativos e resposta padrão simples `Healthy`. Não há verificações de dependências externas. `/` permanece sem endpoint e retorna 404. Respostas não publicam nome da aplicação, paths internos, configuração ou stack traces.

## Validação e limites

Validar `/health` e `/` por requisições HTTP reais, incluindo uma requisição com marcadores sintéticos em header/query/body para conferir que não são registrados pelo baseline de logging. Confirmar binding de Options via inicialização e rejeição de configuração inválida. Todo processo utilizado na validação deve ser encerrado.

Não há projeto de testes ou pacotes de teste nesta etapa. Verificações de referências e pureza do Domain podem ser feitas diretamente sobre os `.csproj`, e o comportamento atual é validado por smoke checks HTTP.

O tratamento de exceções é validado estruturalmente: registro de Problem Details, middleware global, sanitização e diagnósticos. Não é introduzida rota para lançar exceções; a verificação automatizada de uma falha HTTP não tratada fica para quando existir comportamento real que a justifique.

Checks executados na TASK 0.3:

- `GET /health`: 200, `text/plain`, `Healthy`.
- `GET /`: 404, `application/problem+json`, sem detalhes internos.
- `POST /health`: 405; somente GET está autorizado.
- Método, path, status e duração observados nos logs; marcadores sintéticos de Authorization, cookie, query e body ausentes.
- Nome padrão carregado via Options; override CLI com whitespace rejeitado antes de atender HTTP.
- API encerrada com Ctrl+C; processo e listener ausentes após os checks.
- Restore/build backend e lint/build frontend passaram; backend sem erros ou warnings.

## Comandos

Na raiz:

```sh
dotnet restore VortexTrading.slnx
dotnet build VortexTrading.slnx --no-restore
dotnet run --project src/VortexTrading.Api --no-build --no-launch-profile --urls http://127.0.0.1:5187
```

Use uma porta local livre. Encerre a API após a validação. Em `apps/web`, executar `npm run lint` e `npm run build` confirma a regressão do frontend sem alterar sua implementação.

## Referências

- [ASP.NET Core error handling](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling?view=aspnetcore-10.0)
- [ASP.NET Core Options Pattern](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options?view=aspnetcore-10.0)
- [ASP.NET Core HTTP logging](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/http-logging/?view=aspnetcore-10.0)
