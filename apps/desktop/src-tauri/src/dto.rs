use rust_decimal::Decimal;
use serde::{Deserialize, Deserializer, Serialize, Serializer};

use crate::error::{ApiError, ApiErrorKind};

/// Decimal exato no fio. O JSON é a string original. O TypeScript gerado é `string`.
#[derive(Clone, Debug, PartialEq, Eq, ts_rs::TS)]
#[ts(type = "string", export)]
pub struct DecimalString(String);

impl DecimalString {
    pub fn as_str(&self) -> &str {
        &self.0
    }

    pub fn parse(value: &str) -> Result<Self, ApiError> {
        if !plain_decimal(value) {
            return Err(ApiError::contract("Valor decimal inválido."));
        }
        let scale = value
            .split_once('.')
            .map(|(_, fraction)| fraction.len())
            .unwrap_or(0);
        if scale > 28 {
            return Err(ApiError::contract("Valor decimal inválido."));
        }
        value
            .parse::<Decimal>()
            .map(|_| Self(value.to_owned()))
            .map_err(|_| ApiError::contract("Valor decimal inválido."))
    }
}

impl Serialize for DecimalString {
    fn serialize<S: Serializer>(&self, serializer: S) -> Result<S::Ok, S::Error> {
        serializer.serialize_str(&self.0)
    }
}

impl<'de> Deserialize<'de> for DecimalString {
    fn deserialize<D: Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
        let raw = String::deserialize(deserializer)?;
        Self::parse(&raw).map_err(serde::de::Error::custom)
    }
}

/// Fator de lucro e payoff ausentes chegam como `""`. Os outros decimais continuam obrigatórios.
fn blank_or_decimal<'de, D>(deserializer: D) -> Result<DecimalString, D::Error>
where
    D: Deserializer<'de>,
{
    let raw = String::deserialize(deserializer)?;
    if raw.is_empty() {
        return Ok(DecimalString(String::new()));
    }
    DecimalString::parse(&raw).map_err(serde::de::Error::custom)
}

fn plain_decimal(value: &str) -> bool {
    let digits = value.strip_prefix('-').unwrap_or(value);
    let mut parts = digits.split('.');
    let Some(whole) = parts.next() else {
        return false;
    };
    let fraction = parts.next();
    if parts.next().is_some()
        || whole.is_empty()
        || !whole.bytes().all(|byte| byte.is_ascii_digit())
    {
        return false;
    }
    match fraction {
        None => true,
        Some(fraction) => {
            !fraction.is_empty() && fraction.bytes().all(|byte| byte.is_ascii_digit())
        }
    }
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize, ts_rs::TS)]
#[serde(rename_all = "camelCase")]
#[ts(export)]
pub struct MarketQuote {
    pub ticker: String,
    pub name: String,
    pub category: String,
    pub instrument_id: String,
    pub last_price: DecimalString,
    pub change_percent: DecimalString,
    pub change_points: DecimalString,
    pub min: DecimalString,
    pub max: DecimalString,
    pub volume: DecimalString,
    pub updated_at: String,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize, ts_rs::TS)]
#[serde(rename_all = "camelCase")]
#[ts(export)]
pub struct Fill {
    pub execution_id: String,
    pub quantity: DecimalString,
    pub price: DecimalString,
    pub executed_at: String,
}

/// Execução nova vista no diff do snapshot. Quantidade e preço continuam string.
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize, ts_rs::TS)]
#[serde(rename_all = "camelCase")]
#[ts(export)]
pub struct FillNotice {
    pub order_id: String,
    pub ticker: String,
    pub side: String,
    pub quantity: DecimalString,
    pub price: DecimalString,
    pub executed_at: String,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize, ts_rs::TS)]
#[serde(rename_all = "camelCase")]
#[ts(export)]
pub struct Order {
    pub order_id: String,
    pub instrument_id: String,
    pub side: String,
    pub r#type: String,
    pub origin: String,
    pub quantity: DecimalString,
    pub limit_price: Option<DecimalString>,
    pub time_in_force: String,
    pub status: String,
    pub filled_quantity: DecimalString,
    pub remaining_quantity: DecimalString,
    pub rejection_reason: Option<String>,
    pub created_at: String,
    pub last_updated_at: String,
    pub completed_at: Option<String>,
    pub fills: Vec<Fill>,
    #[serde(default)]
    pub role: Option<String>,
}

impl Order {
    /// HTTP 201 com `status == "rejected"` não é ordem aceita.
    pub fn rejected_error(&self) -> Option<ApiError> {
        if self.status != "rejected" {
            return None;
        }
        Some(ApiError {
            kind: ApiErrorKind::Rejected,
            code: self
                .rejection_reason
                .clone()
                .or_else(|| Some("rejected".into())),
            title: self
                .rejection_reason
                .clone()
                .unwrap_or_else(|| "A ordem foi recusada.".into()),
            status: Some(201),
            trace_id: None,
        })
    }
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize, ts_rs::TS)]
#[serde(rename_all = "camelCase")]
#[ts(export)]
pub struct ProtectionRequest {
    pub gain: String,
    pub loss: String,
    pub unit: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub trail: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub break_even: Option<String>,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize, ts_rs::TS)]
#[serde(rename_all = "camelCase")]
#[ts(export)]
pub struct OrderRequest {
    pub instrument_id: String,
    pub side: String,
    pub r#type: Option<String>,
    pub quantity: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub limit_price: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub time_in_force: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub origin: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub protection: Option<ProtectionRequest>,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize, ts_rs::TS)]
#[serde(rename_all = "camelCase")]
#[ts(export)]
pub struct ChartPreviewRequest {
    pub ticker: String,
    pub side: String,
    pub price: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub quantity: Option<String>,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize, ts_rs::TS)]
#[serde(rename_all = "camelCase")]
#[ts(export)]
pub struct ChartPreview {
    pub ticker: String,
    pub side: String,
    pub r#type: String,
    pub price: DecimalString,
    pub reference_close: DecimalString,
    pub notional: DecimalString,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize, ts_rs::TS)]
#[serde(rename_all = "camelCase")]
#[ts(export)]
pub struct ProtectionLevelRequest {
    pub instrument_id: String,
    pub price: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub origin: Option<String>,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize, ts_rs::TS)]
#[serde(rename_all = "camelCase")]
#[ts(export)]
pub struct PositionAction {
    pub status: String,
    #[serde(default)]
    pub order: Option<Order>,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize, ts_rs::TS)]
#[serde(rename_all = "camelCase")]
#[ts(export)]
pub struct EmergencyFlat {
    pub status: String,
    pub cancelled_orders: i32,
    pub flattened_positions: i32,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize, ts_rs::TS)]
#[serde(rename_all = "camelCase")]
#[ts(export)]
pub struct ResetAck {
    pub status: String,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize, ts_rs::TS)]
#[ts(export)]
pub enum DangerAction {
    #[serde(rename = "reset")]
    #[ts(rename = "reset")]
    Reset,
    #[serde(rename = "emergency_flat")]
    #[ts(rename = "emergency_flat")]
    EmergencyFlat,
    #[serde(rename = "reverse")]
    #[ts(rename = "reverse")]
    Reverse,
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize, ts_rs::TS)]
#[serde(rename_all = "camelCase")]
#[ts(export)]
pub struct DangerToken {
    pub token: String,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize, ts_rs::TS)]
#[serde(rename_all = "camelCase")]
#[ts(export)]
pub struct Position {
    pub instrument_id: String,
    pub quantity: DecimalString,
    pub total_cost: DecimalString,
    pub average_cost: DecimalString,
    pub realized_profit_and_loss: DecimalString,
    pub unrealized_profit_and_loss: DecimalString,
    pub total_profit_and_loss: DecimalString,
    pub is_closed: bool,
    pub updated_at: Option<String>,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize, ts_rs::TS)]
#[serde(rename_all = "camelCase")]
#[ts(export)]
pub struct Portfolio {
    pub cash: DecimalString,
    pub equity: DecimalString,
    pub total_cost: DecimalString,
    pub realized_profit_and_loss: DecimalString,
    pub unrealized_profit_and_loss: DecimalString,
    pub total_profit_and_loss: DecimalString,
    pub positions: Vec<Position>,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize, ts_rs::TS)]
#[serde(rename_all = "camelCase")]
#[ts(export)]
pub struct NamedTrade {
    pub ticker: String,
    pub pnl: DecimalString,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize, ts_rs::TS)]
#[serde(rename_all = "camelCase")]
#[ts(export)]
pub struct EquityPoint {
    pub time: String,
    pub equity: DecimalString,
    pub realized_pnl: DecimalString,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize, ts_rs::TS)]
#[serde(rename_all = "camelCase")]
#[ts(export)]
pub struct PerformanceReport {
    pub starting_balance: DecimalString,
    pub current_balance: DecimalString,
    pub equity: DecimalString,
    pub realized_pnl: DecimalString,
    pub daily_pnl: DecimalString,
    pub win_rate: DecimalString,
    pub wins_count: i32,
    pub losses_count: i32,
    pub total_trades: i32,
    #[serde(deserialize_with = "blank_or_decimal")]
    pub profit_factor: DecimalString,
    #[serde(deserialize_with = "blank_or_decimal")]
    pub payoff: DecimalString,
    pub max_drawdown: DecimalString,
    pub best_trade: Option<NamedTrade>,
    pub worst_trade: Option<NamedTrade>,
    pub equity_curve: Vec<EquityPoint>,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize, ts_rs::TS)]
#[serde(rename_all = "camelCase")]
#[ts(export)]
pub struct CsvExport {
    pub row_count: i32,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize, ts_rs::TS)]
#[serde(rename_all = "camelCase")]
#[ts(export)]
pub struct ClosedTrade {
    pub id: String,
    pub timestamp: String,
    pub ticker: String,
    pub market: String,
    pub r#type: String,
    pub quantity: DecimalString,
    pub entry_price: DecimalString,
    pub exit_price: DecimalString,
    pub pnl: DecimalString,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize, ts_rs::TS)]
#[serde(rename_all = "camelCase")]
#[ts(export)]
pub struct TerminalSnapshot {
    pub simulated_time: Option<String>,
    pub session_open: Option<bool>,
    pub quotes: Vec<MarketQuote>,
    pub portfolio: Portfolio,
    pub orders: Vec<Order>,
    pub performance: PerformanceReport,
    pub closed_trades: Vec<ClosedTrade>,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize, ts_rs::TS)]
#[serde(rename_all = "camelCase")]
#[ts(export)]
pub struct Candle {
    pub open_time: String,
    pub close_time: String,
    pub open: DecimalString,
    pub high: DecimalString,
    pub low: DecimalString,
    pub close: DecimalString,
    pub volume: DecimalString,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize, ts_rs::TS)]
#[ts(export)]
pub enum HistoryInterval {
    #[serde(rename = "1d")]
    #[ts(rename = "1d")]
    D1,
    #[serde(rename = "1h")]
    #[ts(rename = "1h")]
    H1,
    #[serde(rename = "15m")]
    #[ts(rename = "15m")]
    M15,
    #[serde(rename = "5m")]
    #[ts(rename = "5m")]
    M5,
    #[serde(rename = "1m")]
    #[ts(rename = "1m")]
    M1,
}

impl HistoryInterval {
    pub fn parse(value: &str) -> Option<Self> {
        match value {
            "1d" => Some(Self::D1),
            "1h" => Some(Self::H1),
            "15m" => Some(Self::M15),
            "5m" => Some(Self::M5),
            "1m" => Some(Self::M1),
            _ => None,
        }
    }

    pub fn as_str(self) -> &'static str {
        match self {
            Self::D1 => "1d",
            Self::H1 => "1h",
            Self::M15 => "15m",
            Self::M5 => "5m",
            Self::M1 => "1m",
        }
    }
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize, ts_rs::TS)]
#[serde(rename_all = "camelCase")]
#[ts(export)]
pub struct YahooHistory {
    pub ticker: String,
    pub yahoo_symbol: String,
    pub interval: String,
    pub currency: String,
    pub delayed: bool,
    pub decimals: i32,
    pub tick_size: DecimalString,
    pub series_name: String,
    pub note: String,
    pub candles: Vec<Candle>,
    pub forming_minutes: Vec<Candle>,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize, ts_rs::TS)]
#[serde(rename_all = "camelCase")]
#[ts(export)]
pub struct IndicatorPoint {
    pub time: String,
    pub value: DecimalString,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize, ts_rs::TS)]
#[serde(rename_all = "camelCase")]
#[ts(export)]
pub struct IndicatorSeries {
    pub name: String,
    pub points: Vec<IndicatorPoint>,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize, ts_rs::TS)]
#[serde(rename_all = "camelCase")]
#[ts(export)]
pub struct Indicator {
    pub id: String,
    /// Parâmetros de indicador. A API manda inteiros e, no Bollinger, um multiplicador numérico.
    /// Não é saldo nem preço de ordem.
    #[ts(type = "unknown")]
    pub parameters: serde_json::Value,
    pub series: Vec<IndicatorSeries>,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize, ts_rs::TS)]
#[serde(rename_all = "camelCase")]
#[ts(export)]
pub struct ChartContext {
    pub instrument_id: String,
    pub interval: String,
    pub source_id: String,
    pub is_demo: bool,
    pub bar_count: i32,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize, ts_rs::TS)]
#[serde(rename_all = "camelCase")]
#[ts(export)]
pub struct ChartSnapshot {
    pub context: ChartContext,
    pub candles: Vec<Candle>,
    pub indicators: Vec<Indicator>,
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize, ts_rs::TS)]
#[ts(export)]
pub enum EmptyStatus {
    #[serde(rename = "empty")]
    Empty,
}

#[derive(Clone, Debug, PartialEq, Serialize, ts_rs::TS)]
#[serde(untagged)]
#[ts(export, untagged)]
pub enum ChartBody {
    Empty { status: EmptyStatus },
    Snapshot(ChartSnapshot),
}

impl<'de> Deserialize<'de> for ChartBody {
    fn deserialize<D: Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
        let value = serde_json::Value::deserialize(deserializer)?;
        parse_chart_value(value).map_err(serde::de::Error::custom)
    }
}

pub fn parse_chart_body(bytes: &[u8]) -> Result<ChartBody, ApiError> {
    let value = serde_json::from_slice(bytes)
        .map_err(|_| ApiError::contract("A resposta da API não cabe no contrato do cliente."))?;
    parse_chart_value(value)
}

fn parse_chart_value(value: serde_json::Value) -> Result<ChartBody, ApiError> {
    if let Some(object) = value.as_object() {
        if object.len() == 1 && object.get("status").and_then(|item| item.as_str()) == Some("empty")
        {
            return Ok(ChartBody::Empty {
                status: EmptyStatus::Empty,
            });
        }
    }
    serde_json::from_value::<ChartSnapshot>(value)
        .map(ChartBody::Snapshot)
        .map_err(|_| ApiError::contract("A resposta da API não cabe no contrato do cliente."))
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize, ts_rs::TS)]
#[serde(rename_all = "camelCase")]
#[ts(export)]
pub enum Theme {
    Light,
    Dark,
    System,
}

impl Theme {
    pub fn parse(value: &str) -> Option<Self> {
        match value {
            "light" => Some(Self::Light),
            "dark" => Some(Self::Dark),
            "system" => Some(Self::System),
            _ => None,
        }
    }

    pub fn as_str(self) -> &'static str {
        match self {
            Self::Light => "light",
            Self::Dark => "dark",
            Self::System => "system",
        }
    }
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize, ts_rs::TS)]
#[serde(rename_all = "camelCase")]
#[ts(export)]
pub struct DesktopConfig {
    pub scheme: String,
    pub host: String,
    pub theme: Theme,
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize, ts_rs::TS)]
#[serde(rename_all = "camelCase")]
#[ts(export)]
pub struct HealthText {
    pub status: u16,
    pub body: String,
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize, ts_rs::TS)]
#[serde(rename_all = "camelCase")]
#[ts(export)]
pub struct StatusResponse {
    pub instrument_id: String,
    pub simulated_time: Option<String>,
    pub last_price: Option<DecimalString>,
    pub resting_order_count: i32,
    pub order_count: i32,
    pub filled_order_count: i32,
    pub open_order_count: i32,
    pub rejected_order_count: i32,
    pub fill_count: i32,
    pub traded_quantity: DecimalString,
    pub restored_order_count: i32,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize, ts_rs::TS)]
#[serde(rename_all = "camelCase")]
#[ts(export)]
pub struct DiagnosticReport {
    pub title: String,
    pub health: Option<HealthText>,
    pub status: Option<StatusResponse>,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize, ts_rs::TS)]
#[serde(rename_all = "camelCase")]
#[ts(export)]
pub enum ConnectionStatus {
    Connected,
    Degraded,
    Disconnected,
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize, ts_rs::TS)]
#[serde(rename_all = "camelCase")]
#[ts(export)]
pub struct ConnectionState {
    pub state: ConnectionStatus,
    pub session_open: Option<bool>,
    pub last_success_at: Option<String>,
    // O atraso cabe em number. O teto do poller é 60 s, então não usamos bigint.
    #[ts(type = "number | null")]
    pub retry_in_ms: Option<u64>,
}
