use std::path::{Path, PathBuf};

use tauri::{AppHandle, Manager, State};
use tauri_plugin_dialog::DialogExt;

use crate::commands::{ticker_ok, AppState};
use crate::dto::{ClosedTrade, CsvExport};
use crate::error::ApiError;

#[tauri::command]
pub async fn get_performance(
    state: State<'_, AppState>,
) -> Result<crate::dto::PerformanceReport, ApiError> {
    state.client.clone()?.performance().await
}

#[tauri::command]
pub async fn get_trades(
    ticker: Option<String>,
    state: State<'_, AppState>,
) -> Result<Vec<ClosedTrade>, ApiError> {
    let ticker = normalize_trade_ticker(ticker)?;
    state.client.clone()?.trades(ticker.as_deref()).await
}

#[tauri::command]
pub async fn export_trades_csv(
    ticker: Option<String>,
    app: AppHandle,
    state: State<'_, AppState>,
) -> Result<CsvExport, ApiError> {
    let ticker = normalize_trade_ticker(ticker)?;
    let trades = state.client.clone()?.trades(ticker.as_deref()).await?;
    let rows = i32::try_from(trades.len())
        .map_err(|_| ApiError::contract("A lista de negócios não cabe na exportação."))?;
    let csv = trades_csv(&trades);
    let picked = tauri::async_runtime::spawn_blocking(move || {
        let mut dialog = app
            .dialog()
            .file()
            .set_title("Exportar negócios")
            .set_file_name("negocios.csv")
            .add_filter("CSV", &["csv"]);
        if let Some(window) = app.get_webview_window("main") {
            dialog = dialog.set_parent(&window);
        }
        dialog.blocking_save_file()
    })
    .await
    .map_err(|_| ApiError::contract("O diálogo não abriu."))?;
    let Some(picked) = picked else {
        return Err(ApiError::cancelled());
    };
    let path = picked
        .into_path()
        .map_err(|_| ApiError::contract("O caminho escolhido não é um arquivo."))?;
    finish_export(Some(path), &csv, rows)
}

/// `None`, vazio, `all` e `todos` listam todos os livros. Outro ticker precisa ser alfanumérico.
pub fn normalize_trade_ticker(ticker: Option<String>) -> Result<Option<String>, ApiError> {
    let Some(ticker) = ticker else {
        return Ok(None);
    };
    let trimmed = ticker.trim();
    if trimmed.is_empty()
        || trimmed.eq_ignore_ascii_case("all")
        || trimmed.eq_ignore_ascii_case("todos")
    {
        return Ok(None);
    }
    if !ticker_ok(trimmed) {
        return Err(ApiError::contract("O ticker não é válido."));
    }
    Ok(Some(trimmed.to_owned()))
}

pub fn trades_csv(trades: &[ClosedTrade]) -> String {
    let mut csv =
        String::from("id,timestamp,ticker,market,type,quantity,entryPrice,exitPrice,pnl\n");
    for trade in trades {
        let cells = [
            trade.id.as_str(),
            trade.timestamp.as_str(),
            trade.ticker.as_str(),
            trade.market.as_str(),
            trade.r#type.as_str(),
            trade.quantity.as_str(),
            trade.entry_price.as_str(),
            trade.exit_price.as_str(),
            trade.pnl.as_str(),
        ];
        for (index, cell) in cells.iter().enumerate() {
            if index > 0 {
                csv.push(',');
            }
            csv.push_str(&csv_cell(cell));
        }
        csv.push('\n');
    }
    csv
}

fn csv_cell(value: &str) -> String {
    if value.contains([',', '"', '\n', '\r']) {
        format!("\"{}\"", value.replace('"', "\"\""))
    } else {
        value.to_owned()
    }
}

pub fn finish_export(path: Option<PathBuf>, csv: &str, rows: i32) -> Result<CsvExport, ApiError> {
    let Some(path) = path else {
        return Err(ApiError::cancelled());
    };
    let path = ensure_csv_extension(path);
    write_csv(&path, csv)?;
    Ok(CsvExport { row_count: rows })
}

fn ensure_csv_extension(path: PathBuf) -> PathBuf {
    if path.extension().is_some() {
        path
    } else {
        path.with_extension("csv")
    }
}

fn write_csv(path: &Path, csv: &str) -> Result<(), ApiError> {
    if let Some(parent) = path.parent() {
        if !parent.as_os_str().is_empty() && !parent.is_dir() {
            return Err(ApiError::contract("A pasta escolhida não existe."));
        }
    }
    let temp = temp_beside(path);
    if let Err(error) = std::fs::write(&temp, csv.as_bytes()) {
        let _ = std::fs::remove_file(&temp);
        tracing::debug!(%error, "csv não gravado");
        return Err(ApiError::contract("O CSV não foi gravado."));
    }
    if std::fs::rename(&temp, path).is_err() {
        let removed = std::fs::remove_file(path);
        if removed.is_ok() && std::fs::rename(&temp, path).is_ok() {
            return Ok(());
        }
        let _ = std::fs::remove_file(&temp);
        return Err(ApiError::contract("O CSV não foi gravado."));
    }
    Ok(())
}

fn temp_beside(path: &Path) -> PathBuf {
    let mut name = path.as_os_str().to_owned();
    name.push(".part");
    PathBuf::from(name)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::dto::DecimalString;
    use crate::error::ApiErrorKind;

    fn trade(market: &str, quantity: &str, pnl: &str) -> ClosedTrade {
        ClosedTrade {
            id: "t1".into(),
            timestamp: "2026-10-14T13:00:00.0000000Z".into(),
            ticker: "PETR4".into(),
            market: market.into(),
            r#type: "buy".into(),
            quantity: DecimalString::parse(quantity).unwrap(),
            entry_price: DecimalString::parse("36.50").unwrap(),
            exit_price: DecimalString::parse("36.80").unwrap(),
            pnl: DecimalString::parse(pnl).unwrap(),
        }
    }

    #[test]
    fn csv_keeps_the_server_decimal_and_quotes_a_comma() {
        let csv = trades_csv(&[
            trade("daytrade", "100", "-1.25"),
            trade("day,trade", "2", "30"),
        ]);
        let mut lines = csv.lines();
        assert_eq!(
            lines.next(),
            Some("id,timestamp,ticker,market,type,quantity,entryPrice,exitPrice,pnl")
        );
        assert_eq!(
            lines.next(),
            Some("t1,2026-10-14T13:00:00.0000000Z,PETR4,daytrade,buy,100,36.50,36.80,-1.25")
        );
        assert_eq!(
            lines.next(),
            Some("t1,2026-10-14T13:00:00.0000000Z,PETR4,\"day,trade\",buy,2,36.50,36.80,30")
        );
        assert_eq!(lines.next(), None);
    }

    #[test]
    fn cancel_writes_nothing_and_confirm_writes_the_decimal() {
        let dir = std::env::temp_dir().join(format!("vortex-csv-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let dest = dir.join("negocios");
        let csv = trades_csv(&[trade("daytrade", "100", "-1.25")]);
        let cancelled = finish_export(None, &csv, 1).unwrap_err();
        assert_eq!(cancelled.kind, ApiErrorKind::Cancelled);
        assert!(!dest.with_extension("csv").exists());
        assert!(!dir.join("negocios.csv.part").exists());

        let written = finish_export(Some(dest), &csv, 1).unwrap();
        assert_eq!(written.row_count, 1);
        let stored = dir.join("negocios.csv");
        assert_eq!(std::fs::read_to_string(&stored).unwrap(), csv);
        assert!(!dir.join("negocios.csv.part").exists());
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn trade_ticker_rejects_a_path_and_omits_all() {
        assert!(normalize_trade_ticker(None).unwrap().is_none());
        assert!(normalize_trade_ticker(Some(" todos ".into()))
            .unwrap()
            .is_none());
        assert!(normalize_trade_ticker(Some("ALL".into()))
            .unwrap()
            .is_none());
        assert_eq!(
            normalize_trade_ticker(Some(" PETR4 ".into()))
                .unwrap()
                .as_deref(),
            Some("PETR4")
        );
        assert!(normalize_trade_ticker(Some("../etc".into())).is_err());
    }
}
