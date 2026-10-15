# Contrato IPC do Vortex Desktop

Os DTOs deste arquivo são a forma lógica. No código eles levam `#[derive(Serialize, Deserialize, TS)]`, `#[serde(rename_all = "camelCase")]` e `#[ts(export)]`. O arquivo `apps/desktop/src/bindings/` é gerado e commitado. Editar o `.ts` à mão quebra o teste de exportação.

Requests omitem `None` (`skip_serializing_if`). Responses não usam `deny_unknown_fields`.

Decimal no Rust é `rust_decimal::Decimal` com `serde-with-str`. No TypeScript gerado é `string`.

## ApiError

```rust
enum ApiErrorKind {
    Transport, Timeout, Problem, Rejected, Contract, Cancelled, Danger,
}

struct ApiError {
    kind: ApiErrorKind,
    code: Option<String>,
    title: String,
    status: Option<u16>,
    trace_id: Option<String>,
}
```

Todo command devolve `Result<T, ApiError>`.

ProblemDetails lido do corpo: `title`, `status`, extensão `code`, extensão `traceId`. `detail` da API vem nulo de propósito (`Program.cs`) e o cliente não o exibe.

Ordem com `status == "rejected"` vira `kind: Rejected`, `code` igual a `rejectionReason` quando existir, `title` igual a esse motivo ou "A ordem foi recusada." O HTTP 201 não é sucesso nesse caso. O mesmo vale para o `order` interno de flatten e reverse.

## Timeout e retry

| Classe | Prazo | Retry |
| --- | --- | --- |
| GET em geral, inclusive `get_chart` e `get_terminal` | 5 s | Até 2 repetições, 200 ms e 500 ms, jitter de 20%. Só se não houve corpo HTTP |
| `get_history`, `chart_preview`, `place_protection` | 12 s | `get_history` segue a regra de GET. Os dois POST não repetem |
| `flatten`, `reverse`, `emergency_flat`, `reset_session` | 12 s | Não repetem |
| Demais POST | 5 s | Não repetem |

O poller não usa o retry do command. Ele tem o backoff da arquitetura e no máximo uma `GET /api/terminal` em voo.

Header enviado em toda chamada, ignorado pela API de hoje: `X-Vortex-Client: vortex-desktop/<versão do tauri.conf.json>`.

## Commands

Assinatura Rust. O binding TS tem o mesmo nome em camelCase via `invoke`.

### Config e saúde

```rust
fn get_config() -> Result<DesktopConfig, ApiError>;
fn set_base_url(url: String) -> Result<DesktopConfig, ApiError>;
fn set_theme(theme: Theme) -> Result<DesktopConfig, ApiError>;
fn health_live() -> Result<HealthText, ApiError>;
fn health_ready() -> Result<HealthText, ApiError>;
fn get_status() -> Result<StatusResponse, ApiError>;
```

`set_base_url` aceita só `http` e `https`, sem usuário na URL, sem caminho além de `/`. Produção esperada: `https://vortexsoftware.tech`. Dev: `http://127.0.0.1:5187`.

`HealthText` é o corpo texto `Healthy` ou o status 503. Não é JSON de negócio.

`StatusResponse` espelha o record da API: `instrumentId`, `simulatedTime?`, `lastPrice?`, contagens, `tradedQuantity` decimal.

### Snapshot e mercado

```rust
fn get_terminal() -> Result<TerminalSnapshot, ApiError>;
fn get_quotes() -> Result<Vec<MarketQuote>, ApiError>;
fn get_depth(ticker: Option<String>) -> Result<MarketDepth, ApiError>;
fn get_history(ticker: String, interval: HistoryInterval, max_bars: Option<u32>) -> Result<YahooHistory, ApiError>;
fn get_chart(ticker: Option<String>, interval: Option<ChartInterval>, max_bars: Option<u32>) -> Result<ChartBody, ApiError>;
```

```rust
enum HistoryInterval { #[serde(rename = "1d")] D1, #[serde(rename = "1h")] H1,
    #[serde(rename = "15m")] M15, #[serde(rename = "5m")] M5, #[serde(rename = "1m")] M1 }
enum ChartInterval { #[serde(rename = "1m")] M1, #[serde(rename = "5m")] M5,
    #[serde(rename = "15m")] M15, #[serde(rename = "60m")] M60, #[serde(rename = "1h")] H1 }

#[serde(untagged)]
enum ChartBody { Empty { status: EmptyStatus }, Snapshot(ChartSnapshot) }
enum EmptyStatus { #[serde(rename = "empty")] Empty }
```

`Empty` é testado antes na deserialização. Um snapshot de verdade não tem `status: "empty"` como único contrato; se os dois falharem, o erro é `contract`.

`TerminalSnapshot`: `simulatedTime`, `sessionOpen`, `quotes`, `portfolio`, `orders`, `performance`, `closedTrades`. Os objetos internos seguem `TradingContracts.cs`, `MarketQuotesContracts` e `ReportContracts.cs`. Campos financeiros são decimal-string. `sessionOpen` é `Option<bool>`.

`MarketQuote` inclui `instrumentId`. É daqui que a boleta tira o GUID. Não existe tabela de GUID no cliente.

### Ordens

```rust
fn submit_order(request: OrderRequest) -> Result<Order, ApiError>;
fn chart_preview(request: ChartPreviewRequest) -> Result<ChartPreview, ApiError>;
fn revise_order(order_id: String, price: Decimal) -> Result<Order, ApiError>;
fn cancel_order(order_id: String) -> Result<Order, ApiError>;
fn place_protection(request: ProtectionLevelRequest) -> Result<Order, ApiError>;
fn flatten(instrument_id: String) -> Result<PositionAction, ApiError>;
fn reverse(instrument_id: String, danger_token: String) -> Result<PositionAction, ApiError>;
fn emergency_flat(danger_token: String) -> Result<EmergencyFlat, ApiError>;
fn reset_session(danger_token: String) -> Result<ResetAck, ApiError>;
fn confirm_danger(action: DangerAction) -> Result<DangerToken, ApiError>;
```

`OrderRequest` é o record da API: `instrumentId`, `side` (`buy` | `sell`), `type` opcional (`market` | `limit`), `quantity`, `limitPrice` opcional, `timeInForce` opcional, `origin` opcional (`session` | `chart`), `protection` opcional. Mercado sem `limitPrice`. Quantidade e preços são decimal-string. `protection.unit` é `ticks` | `points` | `percent`.

Validação no command, antes do HTTP:

- `instrumentId` com forma de GUID;
- `side` e `type` no enum;
- `quantity` decimal e maior que zero;
- `limit` exige `limitPrice`; `market` recusa `limitPrice`;
- `origin` vazio vira omissão, não a string `""`.

`ChartPreviewRequest`: `ticker`, `side`, `price`, `quantity?`. Resposta: `ticker`, `side`, `type` (`limit` | `stop`), `price`, `referenceClose`, `notional`.

`PositionAction`: `status` (`flat` | `sent`) e `order?`. Se `order.status == "rejected"`, o command devolve `Rejected`.

`EmergencyFlat`: `status` (`ok`), `cancelledOrders` (número), `flattenedPositions` (número).

`ResetAck`: `status` (`ok`).

`DangerAction`: `reset` | `emergency_flat` | `reverse`. `confirm_danger` abre o diálogo nativo. Sim gera um token opaco de 32 bytes, válido por 30 s, de uso único, amarrado à ação. Não devolve a ação ao JavaScript para ele mesmo confirmar. Fechar o diálogo devolve `Cancelled`.

### Relatórios

```rust
fn get_performance() -> Result<PerformanceReport, ApiError>;
fn get_trades(ticker: Option<String>) -> Result<Vec<ClosedTrade>, ApiError>;
fn export_trades_csv(ticker: Option<String>) -> Result<CsvExport, ApiError>;
```

`export_trades_csv` busca os trades, abre o diálogo de salvar e escreve o arquivo. `CsvExport` traz só `rowCount`, depois da escrita. O caminho fica no Rust e não volta para o JavaScript. Cancelar o diálogo é `Cancelled` e não cria arquivo. O CSV usa os decimais como a API mandou, separador `,`, cabeçalho fixo: `id,timestamp,ticker,market,type,quantity,entryPrice,exitPrice,pnl`.

### Atualização, só na F5

```rust
fn check_update() -> Result<UpdateOffer, ApiError>;
fn install_update() -> Result<(), ApiError>;
```

Sem endpoint da API de trading. O manifesto é o do `tauri-plugin-updater`.

## Events

| Evento | Payload | Quem emite |
| --- | --- | --- |
| `terminal://snapshot` | `TerminalSnapshot` | Poller, a cada geração nova |
| `terminal://connection` | `ConnectionState` | Poller |
| `orders://fill` | `FillNotice` | Poller, quando um `executionId` aparece que não estava no snapshot anterior |
| `desktop://menu` | `string`, o id do item | Menu da estação. A página troca a visão ou pede o CSV que já existe |
| `desktop://notice` | `string` | Texto depois do diálogo de Operar. Esse caminho não envia a ação |

```rust
struct ConnectionState {
    state: ConnectionStatus, // connected | degraded | disconnected
    session_open: Option<bool>,
    last_success_at: Option<String>,
    retry_in_ms: Option<u64>,
}

struct FillNotice {
    order_id: String,
    ticker: String,
    side: String,
    quantity: DecimalString,
    price: DecimalString,
    executed_at: String,
}
```

O fill é diff local do snapshot. Não é push do servidor. O primeiro snapshot só grava os `executionId`. A notificação nativa usa `ticker`, lado e preço já formatados para leitura. O som e o clique não enviam ordem. O clique só foca a janela `main`.

Não há evento `terminal://snapshot` parcial. Snapshot atrasado não é emitido.

## code para a pessoa

Títulos abaixo são os que a API manda hoje, quando o código os fixa. Se o `title` vier preenchido, ele ganha da tabela. A tabela cobre `title` vazio e o log.

| code | HTTP | Mensagem quando o title não vier |
| --- | --- | --- |
| `invalid_request` | 400 | A ordem não foi aceita. Confira quantidade, preço e tipo. |
| `interval_not_supported` | 400 | Este intervalo de gráfico não existe. |
| `instrument_not_found` | 404 | Este ativo não está no pregão. |
| `order_not_found` | 404 | Esta ordem não está mais na sessão. |
| `order_state_conflict` | 409 | A ordem mudou e esta ação não cabe mais. |
| `domain_rule_violation` | 422 | A ordem viola uma regra de negócio. |
| `yahoo_symbol_unavailable` | 422 | O Yahoo Finance não lista este contrato. |
| `chart_series_unavailable` | 503 | A série do gráfico não respondeu. |
| `yahoo_unavailable` | 503 | A cotação do Yahoo não respondeu. |
| `internal_error` | 500 | A operação não foi concluída. |
| `rejected` | 201 | A ordem foi recusada. |
| — timeout | — | A API não respondeu a tempo. |
| — transport | — | Sem conexão com a API. |

`traceId` não aparece na boleta. Aparece no log e numa linha "detalhes" copiável no diálogo de erro.
