use tauri::{AppHandle, Emitter, Manager, State};

use crate::client::ApiClient;
use crate::danger::DangerGate;
use crate::dto::{
    ChartBody, DesktopConfig, DiagnosticReport, HealthText, HistoryInterval, StatusResponse,
    TerminalSnapshot, Theme, YahooHistory,
};
use crate::error::ApiError;
use crate::settings;

pub struct AppState {
    pub client: Result<ApiClient, ApiError>,
    pub danger: DangerGate,
}

const CHART_INTERVALS: &[&str] = &["1m", "5m", "15m", "60m", "1h"];
const HISTORY_DEFAULT_BARS: u32 = 180;

#[tauri::command]
pub async fn get_terminal(state: State<'_, AppState>) -> Result<TerminalSnapshot, ApiError> {
    state.client.clone()?.terminal().await
}

#[tauri::command]
pub async fn get_chart(
    ticker: Option<String>,
    interval: Option<String>,
    max_bars: Option<u32>,
    state: State<'_, AppState>,
) -> Result<ChartBody, ApiError> {
    let (ticker, interval, max_bars) = normalize_chart(ticker, interval, max_bars)?;
    state
        .client
        .clone()?
        .chart(&ticker, &interval, max_bars)
        .await
}

#[tauri::command]
pub async fn get_history(
    ticker: String,
    interval: String,
    max_bars: Option<u32>,
    state: State<'_, AppState>,
) -> Result<YahooHistory, ApiError> {
    let (ticker, interval, max_bars) = normalize_history(ticker, interval, max_bars)?;
    state
        .client
        .clone()?
        .history(&ticker, interval.as_str(), max_bars)
        .await
}

#[tauri::command]
pub fn get_config(app: AppHandle, state: State<'_, AppState>) -> Result<DesktopConfig, ApiError> {
    let mut config = state.client.clone()?.config();
    config.theme = settings::read_theme(&app);
    Ok(config)
}

#[tauri::command]
pub fn set_theme(theme: Theme, app: AppHandle) -> Result<DesktopConfig, ApiError> {
    assign_theme(&app, theme)
}

#[tauri::command]
pub async fn health_live(state: State<'_, AppState>) -> Result<HealthText, ApiError> {
    state.client.clone()?.health("health/live").await
}

#[tauri::command]
pub async fn health_ready(state: State<'_, AppState>) -> Result<HealthText, ApiError> {
    state.client.clone()?.health("health/ready").await
}

#[tauri::command]
pub async fn get_status(state: State<'_, AppState>) -> Result<StatusResponse, ApiError> {
    state.client.clone()?.status().await
}

pub fn assign_theme(app: &AppHandle, theme: Theme) -> Result<DesktopConfig, ApiError> {
    if let Err(error) = settings::write_theme(app, theme) {
        emit_failure(app, error.title.clone());
        return Err(error);
    }
    if let Some(window) = app.get_webview_window("main") {
        settings::apply_window_theme(&window, theme);
    }
    let client = app.state::<AppState>().client.clone();
    let mut config = match client {
        Ok(client) => client.config(),
        Err(error) => {
            emit_failure(app, error.title.clone());
            return Err(error);
        }
    };
    config.theme = theme;
    let _ = app.emit("desktop://config", &config);
    Ok(config)
}

fn emit_failure(app: &AppHandle, title: String) {
    let _ = app.emit(
        "desktop://diagnostic",
        DiagnosticReport {
            title,
            health: None,
            status: None,
        },
    );
}

fn normalize_chart(
    ticker: Option<String>,
    interval: Option<String>,
    max_bars: Option<u32>,
) -> Result<(String, String, u32), ApiError> {
    let ticker = ticker.unwrap_or_else(|| "PETR4".to_owned());
    if !ticker_ok(&ticker) {
        return Err(ApiError::contract("O ticker não é válido."));
    }
    let interval = interval.unwrap_or_else(|| "5m".to_owned());
    if !CHART_INTERVALS.contains(&interval.as_str()) {
        return Err(ApiError::contract("Este intervalo de gráfico não existe."));
    }
    Ok((ticker, interval, max_bars.unwrap_or(200).clamp(1, 500)))
}

fn normalize_history(
    ticker: String,
    interval: String,
    max_bars: Option<u32>,
) -> Result<(String, HistoryInterval, u32), ApiError> {
    if !ticker_ok(&ticker) {
        return Err(ApiError::contract("O ticker não é válido."));
    }
    let interval = HistoryInterval::parse(&interval)
        .ok_or_else(|| ApiError::contract("O intervalo precisa ser 1d, 1h, 15m, 5m ou 1m."))?;
    Ok((
        ticker,
        interval,
        max_bars.unwrap_or(HISTORY_DEFAULT_BARS).clamp(1, 500),
    ))
}

pub(crate) fn ticker_ok(ticker: &str) -> bool {
    let bytes = ticker.as_bytes();
    (1..=16).contains(&bytes.len()) && bytes.iter().all(|byte| byte.is_ascii_alphanumeric())
}

#[cfg(test)]
mod tests {
    use super::{normalize_chart, normalize_history};

    #[test]
    fn chart_arguments_are_bounded() {
        let (ticker, interval, bars) = normalize_chart(None, None, None).unwrap();
        assert_eq!(
            (ticker.as_str(), interval.as_str(), bars),
            ("PETR4", "5m", 200)
        );
        assert_eq!(
            normalize_chart(Some("WING26".into()), Some("1h".into()), Some(9_000))
                .unwrap()
                .2,
            500
        );
        assert!(normalize_chart(Some("../etc".into()), None, None).is_err());
        assert!(normalize_chart(Some("PETR4".into()), Some("2m".into()), None).is_err());
    }

    #[test]
    fn history_arguments_are_bounded() {
        let (ticker, interval, bars) =
            normalize_history("PETR4".into(), "1d".into(), None).unwrap();
        assert_eq!(
            (ticker.as_str(), interval.as_str(), bars),
            ("PETR4", "1d", 180)
        );
        assert_eq!(
            normalize_history("vale3".into(), "15m".into(), Some(9_000))
                .unwrap()
                .2,
            500
        );
        assert!(normalize_history("PETR4".into(), "2m".into(), None).is_err());
        assert!(normalize_history("PETR4".into(), "60m".into(), None).is_err());
        assert!(normalize_history("../etc".into(), "1m".into(), None).is_err());
    }
}
