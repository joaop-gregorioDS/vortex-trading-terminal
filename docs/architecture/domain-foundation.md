# Domain Foundation — TASK 0.6

Fundação sobre `615e806` para futuras áreas de Markets e Trading. As primitives protegem identidade textual normalizada, associação monetária à moeda e valores não negativos de preço/quantidade. Não há entidades, agregados, base Entity, repositories, persistência ou integração externa nesta etapa.

## Primitives e invariantes

| Tipo | Representação | Invariantes e semântica |
| --- | --- | --- |
| `DomainException` | Exceção selada com mensagem | Violação explícita de invariantes, sem hierarquia especializada |
| `Symbol` | `string Value` | Rejeita null/vazio/whitespace; aplica `Trim().ToUpperInvariant()` |
| `Currency` | `string Value` | Mesma normalização e validação de presença; identifica a unidade monetária de Money |
| `Money` | `decimal Amount` + `Currency` | Currency obrigatória e válida; valores positivos, zero e negativos permitidos |
| `Price` | `decimal Value` | Preço unitário não negativo, incluindo zero |
| `Quantity` | `decimal Value` | Quantidade não negativa, incluindo zero e frações |

Symbol não impõe regex, tamanho ou convenções de uma exchange. A normalização cobre somente whitespace nas extremidades e case; separadores e demais caracteres internos são preservados. Não verifica existência de instrumento nem fornece identidade global entre exchanges; esse contexto pertencerá ao futuro modelo de Instrument.

Currency não impõe três caracteres nem catálogo ISO 4217, permitindo códigos como `USDT`. Sua validade inicial significa presença de um identificador normalizado, não existência em um registro externo. Restrições adicionais de moeda/unidade pertencerão à fronteira ou caso de uso que precisar delas.

Money implementa somente `+` e `-`: moedas iguais por valor normalizado são obrigatórias; diferença de moeda ou operandos null gera `DomainException`. As operações produzem nova instância e preservam os operandos. Não há comparação de ordenação, conversão FX ou taxa presumida. Igualdade entre moedas distintas retorna false, sem tratar igualdade como conversão ou comparação financeira.

## Semântica de valor e imutabilidade

As cinco primitives são `sealed record` de referência, com propriedades get-only e construtores explícitos. Equality, operadores `==`/`!=` e hashing são gerados pelo C# e incluem os componentes de cada valor. As propriedades não possuem setters/init públicos que permitam contornar validação via `with`.

A escolha de records de referência evita um `default(struct)` que representaria Symbol/Currency ausentes ou Money sem moeda. Referências null continuam possíveis em C#; Money valida Currency e operandos, e os demais tipos validam suas entradas na construção. Não há conversões implícitas de/para primitives.

`ToString()` usa o texto normalizado para Symbol/Currency e `CultureInfo.InvariantCulture` para números; Money usa `amount currency`. Não é formatação localizada de UI nem contrato de serialização.

## Política decimal

Money, Price e Quantity preservam exatamente o decimal fornecido na criação, inclusive sua escala representacional. Não chamam `Round`, não truncam e não impõem número fixo de casas. Testes com múltiplas casas e `decimal.GetBits` verificam essa preservação. Equality decimal continua numérica: `1.25m` e `1.2500m` são iguais e têm hashes compatíveis.

Money usa a aritmética decimal padrão do .NET, com seus limites finitos de precisão; isso não é precisão arbitrária nem uma política de arredondamento por moeda. Overflow produz `OverflowException`, sem saturação ou wrapping silencioso. Não há regra global de positividade de Money.

Tick size, lot/step size, quantidade positiva de uma ordem, regras de arredondamento e `numeric(p,s)` de persistência serão definidos por instrumento/caso de uso quando houver um modelo legítimo, conforme ADR-002. Price e Quantity não carregam Currency nesta etapa; a relação preço/instrumento/moeda será estabelecida no modelo futuro.

## Testes e dependências

Domain permanece sem PackageReference, FrameworkReference ou ProjectReference externos. `Domain.Tests` referencia somente Domain e usa assertions nativas do xUnit, com dados fixos e sem banco, Docker, rede, arquivos, relógio ou aleatoriedade nos testes. A cultura de cada teste é restaurada em `finally`.

Dependências diretas exclusivas do projeto de testes, estáveis e verificadas em 2026-10-01:

- `xunit.v3.mtp-off 4.0.1`: framework/assertions xUnit v3 com MTP desabilitado.
- `xunit.runner.visualstudio 4.0.0`: descoberta/execução no VSTest, com assets privados.
- `Microsoft.NET.Test.Sdk 18.10.1`: integração de `dotnet test` com VSTest.

Os pacotes suportam .NET 8+, incluindo `net10.0`. VSTest permite manter `global.json` e o SDK aprovados. A variante `mtp-off` evita dependências de uma segunda plataforma de testes. Não foram adicionados FluentAssertions, mocking ou collector de cobertura; dependências transitivas oficiais do Test SDK permanecem gerenciadas pelo pacote.

Fontes: [pacotes xUnit v3](https://xunit.net/docs/nuget-packages-v3), [seleção MTP/VSTest](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform), [xUnit NuGet](https://www.nuget.org/packages/xunit.v3.mtp-off/4.0.1), [runner NuGet](https://www.nuget.org/packages/xunit.runner.visualstudio/4.0.0) e [Test SDK NuGet](https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/18.10.1).

Execute na raiz:

```powershell
dotnet restore VortexTrading.slnx
dotnet build VortexTrading.slnx --no-restore
dotnet test tests/backend/VortexTrading.Domain.Tests/VortexTrading.Domain.Tests.csproj --no-build
dotnet test VortexTrading.slnx --no-build
```

Testes cobrem normalização/presença, igualdade/hash, criação e operações monetárias, mismatch de moeda, null, overflow, preços/quantidades zero/positivos/negativos, frações, escala preservada e culturas `tr-TR`/`pt-BR`. Artefatos e TestResults continuam ignorados pelo Git.

## Fronteiras e decisões adiadas

DbContext, DbSets, mappings, migrations, API e frontend permanecem inalterados. As primitives não possuem atributos/converters JSON, EF ou HTTP. Redis continua desacoplado. Nenhum teste de domínio utiliza PostgreSQL ou Redis; nesta tarefa os containers foram somente consultados no pre-flight/estado final.

A política temporal será definida com Market Data: timestamps deverão ter semântica explícita e determinística, distinguindo instante do evento, fonte e demais tempos necessários ao caso de uso. Nenhum wrapper de DateTime, DateTimeOffset ou TimeSpan foi criado agora. Essas decisões e o primeiro modelo persistente exigem revisão arquitetural própria.
