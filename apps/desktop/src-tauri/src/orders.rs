use rust_decimal::Decimal;
use tauri::{AppHandle, State};

use crate::commands::AppState;
use crate::danger;
use crate::dto::{
    ChartPreview, ChartPreviewRequest, DangerAction, DangerToken, DecimalString, EmergencyFlat,
    Order, OrderRequest, PositionAction, ProtectionLevelRequest, ProtectionRequest, ResetAck,
};
use crate::error::ApiError;

#[tauri::command]
pub async fn submit_order(
    request: OrderRequest,
    state: State<'_, AppState>,
) -> Result<Order, ApiError> {
    let request = normalize_order(request)?;
    accept_order(state.client.clone()?.submit_order(&request).await?)
}

#[tauri::command]
pub async fn chart_preview(
    request: ChartPreviewRequest,
    state: State<'_, AppState>,
) -> Result<ChartPreview, ApiError> {
    let request = normalize_preview(request)?;
    state.client.clone()?.chart_preview(&request).await
}

#[tauri::command]
pub async fn revise_order(
    order_id: String,
    price: String,
    state: State<'_, AppState>,
) -> Result<Order, ApiError> {
    if !guid_ok(&order_id) {
        return Err(ApiError::contract("A ordem não é válida."));
    }
    let price = positive_decimal(&price, "O preço precisa ser maior que zero.")?;
    accept_order(
        state
            .client
            .clone()?
            .revise_order(&order_id, &price)
            .await?,
    )
}

#[tauri::command]
pub async fn cancel_order(order_id: String, state: State<'_, AppState>) -> Result<Order, ApiError> {
    if !guid_ok(&order_id) {
        return Err(ApiError::contract("A ordem não é válida."));
    }
    accept_order(state.client.clone()?.cancel_order(&order_id).await?)
}

#[tauri::command]
pub async fn place_protection(
    request: ProtectionLevelRequest,
    state: State<'_, AppState>,
) -> Result<Order, ApiError> {
    let request = normalize_level(request)?;
    accept_order(state.client.clone()?.place_protection(&request).await?)
}

#[tauri::command]
pub async fn flatten(
    instrument_id: String,
    state: State<'_, AppState>,
) -> Result<PositionAction, ApiError> {
    let instrument_id = require_guid(instrument_id, "O instrumento não é válido.")?;
    accept_action(state.client.clone()?.flatten(&instrument_id).await?)
}

#[tauri::command]
pub async fn reverse(
    instrument_id: String,
    danger_token: String,
    state: State<'_, AppState>,
) -> Result<PositionAction, ApiError> {
    state.danger.take(DangerAction::Reverse, &danger_token)?;
    let instrument_id = require_guid(instrument_id, "O instrumento não é válido.")?;
    accept_action(state.client.clone()?.reverse(&instrument_id).await?)
}

#[tauri::command]
pub async fn emergency_flat(
    danger_token: String,
    state: State<'_, AppState>,
) -> Result<EmergencyFlat, ApiError> {
    state
        .danger
        .take(DangerAction::EmergencyFlat, &danger_token)?;
    state.client.clone()?.emergency_flat().await
}

#[tauri::command]
pub async fn reset_session(
    danger_token: String,
    state: State<'_, AppState>,
) -> Result<ResetAck, ApiError> {
    state.danger.take(DangerAction::Reset, &danger_token)?;
    state.client.clone()?.reset_session().await
}

#[tauri::command]
pub async fn confirm_danger(
    action: DangerAction,
    app: AppHandle,
    state: State<'_, AppState>,
) -> Result<DangerToken, ApiError> {
    let gate = state.danger.clone();
    danger::confirm(&gate, action, app).await
}

pub fn accept_order(order: Order) -> Result<Order, ApiError> {
    if let Some(error) = order.rejected_error() {
        return Err(error);
    }
    Ok(order)
}

pub fn accept_action(action: PositionAction) -> Result<PositionAction, ApiError> {
    if let Some(error) = action.order.as_ref().and_then(Order::rejected_error) {
        return Err(error);
    }
    Ok(action)
}

fn normalize_order(mut request: OrderRequest) -> Result<OrderRequest, ApiError> {
    request.instrument_id = require_guid(request.instrument_id, "O instrumento não é válido.")?;
    if request.side != "buy" && request.side != "sell" {
        return Err(ApiError::contract("O lado precisa ser compra ou venda."));
    }
    request.quantity = positive_decimal(
        &request.quantity,
        "A quantidade precisa ser maior que zero.",
    )?;
    let origin = normalize_origin(request.origin.take())?;
    let kind = blank(request.r#type.take());
    let limit = blank(request.limit_price.take());
    if kind.as_deref() == Some("stop") && origin.as_deref() != Some("chart") {
        return Err(ApiError::contract("Stop sai do gráfico."));
    }
    request.limit_price = match kind.as_deref() {
        Some("market") => {
            if limit.is_some() {
                return Err(ApiError::contract("Uma ordem a mercado não leva preço."));
            }
            None
        }
        Some("limit") | Some("stop") => {
            let Some(price) = limit else {
                return Err(ApiError::contract("Informe o preço."));
            };
            Some(positive_decimal(
                &price,
                "O preço precisa ser maior que zero.",
            )?)
        }
        _ => {
            return Err(ApiError::contract(
                "O tipo precisa ser mercado, limite ou stop.",
            ))
        }
    };
    request.r#type = kind;
    request.origin = origin;
    request.time_in_force = match blank(request.time_in_force.take()).as_deref() {
        None => None,
        Some("gtc") => Some("gtc".to_owned()),
        Some(_) => return Err(ApiError::contract("A validade precisa ser gtc.")),
    };
    request.protection = normalize_protection(request.protection.take())?;
    Ok(request)
}

fn normalize_preview(mut request: ChartPreviewRequest) -> Result<ChartPreviewRequest, ApiError> {
    let ticker = request.ticker.trim();
    if !ticker_ok(ticker) {
        return Err(ApiError::contract("O ticker não é válido."));
    }
    request.ticker = ticker.to_owned();
    if request.side != "buy" && request.side != "sell" {
        return Err(ApiError::contract("O lado precisa ser compra ou venda."));
    }
    request.price = positive_decimal(&request.price, "O preço precisa ser maior que zero.")?;
    request.quantity = match blank(request.quantity.take()) {
        Some(quantity) => Some(positive_decimal(
            &quantity,
            "A quantidade precisa ser maior que zero.",
        )?),
        None => None,
    };
    Ok(request)
}

fn normalize_level(
    mut request: ProtectionLevelRequest,
) -> Result<ProtectionLevelRequest, ApiError> {
    request.instrument_id = require_guid(request.instrument_id, "O instrumento não é válido.")?;
    request.price = positive_decimal(&request.price, "O preço precisa ser maior que zero.")?;
    request.origin = normalize_origin(request.origin.take())?;
    Ok(request)
}

fn normalize_protection(
    protection: Option<ProtectionRequest>,
) -> Result<Option<ProtectionRequest>, ApiError> {
    let Some(mut protection) = protection else {
        return Ok(None);
    };
    let unit = protection.unit.trim();
    if !matches!(unit, "ticks" | "points" | "percent") {
        return Err(ApiError::contract(
            "A unidade da proteção precisa ser ticks, pontos ou percentual.",
        ));
    }
    protection.unit = unit.to_owned();
    protection.gain = positive_decimal(
        &protection.gain,
        "O ganho da proteção precisa ser maior que zero.",
    )?;
    protection.loss = positive_decimal(
        &protection.loss,
        "A perda da proteção precisa ser maior que zero.",
    )?;
    protection.trail = match blank(protection.trail.take()) {
        Some(value) => Some(positive_decimal(
            &value,
            "A trilha da proteção precisa ser maior que zero.",
        )?),
        None => None,
    };
    protection.break_even = match blank(protection.break_even.take()) {
        Some(value) => Some(positive_decimal(
            &value,
            "O empate da proteção precisa ser maior que zero.",
        )?),
        None => None,
    };
    Ok(Some(protection))
}

fn normalize_origin(origin: Option<String>) -> Result<Option<String>, ApiError> {
    match blank(origin).as_deref() {
        None => Ok(None),
        Some("session") => Ok(Some("session".to_owned())),
        Some("chart") => Ok(Some("chart".to_owned())),
        Some(_) => Err(ApiError::contract(
            "A origem precisa ser sessão ou gráfico.",
        )),
    }
}

fn positive_decimal(value: &str, title: &str) -> Result<String, ApiError> {
    let trimmed = value.trim();
    let parsed = DecimalString::parse(trimmed).map_err(|_| ApiError::contract(title))?;
    let number = parsed
        .as_str()
        .parse::<Decimal>()
        .map_err(|_| ApiError::contract(title))?;
    if number <= Decimal::ZERO {
        return Err(ApiError::contract(title));
    }
    Ok(trimmed.to_owned())
}

fn blank(value: Option<String>) -> Option<String> {
    value.and_then(|item| {
        let trimmed = item.trim();
        if trimmed.is_empty() {
            None
        } else {
            Some(trimmed.to_owned())
        }
    })
}

fn require_guid(value: String, title: &str) -> Result<String, ApiError> {
    let trimmed = value.trim();
    if guid_ok(trimmed) {
        Ok(trimmed.to_owned())
    } else {
        Err(ApiError::contract(title))
    }
}

fn guid_ok(value: &str) -> bool {
    let bytes = value.as_bytes();
    if bytes.len() != 36 || value.bytes().all(|byte| byte == b'0' || byte == b'-') {
        return false;
    }
    let groups = [8usize, 4, 4, 4, 12];
    let mut index = 0;
    for (group, length) in groups.into_iter().enumerate() {
        if group > 0 {
            if bytes.get(index) != Some(&b'-') {
                return false;
            }
            index += 1;
        }
        for _ in 0..length {
            let Some(byte) = bytes.get(index) else {
                return false;
            };
            if !byte.is_ascii_hexdigit() {
                return false;
            }
            index += 1;
        }
    }
    index == bytes.len()
}

fn ticker_ok(ticker: &str) -> bool {
    let bytes = ticker.as_bytes();
    (1..=16).contains(&bytes.len()) && bytes.iter().all(|byte| byte.is_ascii_alphanumeric())
}

#[cfg(test)]
mod tests {
    use super::{accept_action, accept_order, normalize_order, normalize_preview};
    use crate::dto::{Order, OrderRequest, PositionAction};
    use crate::error::ApiErrorKind;

    fn request(kind: &str, price: Option<&str>, quantity: &str) -> OrderRequest {
        OrderRequest {
            instrument_id: "11111111-1111-1111-1111-111111111111".into(),
            side: "buy".into(),
            r#type: Some(kind.into()),
            quantity: quantity.into(),
            limit_price: price.map(str::to_owned),
            time_in_force: None,
            origin: Some("session".into()),
            protection: None,
        }
    }

    #[test]
    fn order_shape_is_checked_before_http() {
        let market = normalize_order(request("market", None, "10")).unwrap();
        assert!(market.limit_price.is_none());
        assert!(market.origin.as_deref() == Some("session"));
        assert!(normalize_order(request("market", Some("10"), "10")).is_err());
        assert!(normalize_order(request("limit", None, "10")).is_err());
        assert!(normalize_order(request("limit", Some("36.50"), "0")).is_err());
        assert!(normalize_order(request("stop", Some("36.50"), "1")).is_err());
        let mut broken = request("limit", Some("36.50"), "1");
        broken.instrument_id = "não".into();
        assert!(normalize_order(broken).is_err());
        let mut empty_origin = request("market", None, "1");
        empty_origin.origin = Some(String::new());
        assert!(normalize_order(empty_origin).unwrap().origin.is_none());
    }

    #[test]
    fn preview_keeps_the_server_price_string() {
        let preview = normalize_preview(crate::dto::ChartPreviewRequest {
            ticker: "PETR4".into(),
            side: "sell".into(),
            price: "36.50".into(),
            quantity: None,
        })
        .unwrap();
        assert_eq!(preview.price, "36.50");
        assert!(normalize_preview(crate::dto::ChartPreviewRequest {
            ticker: "../etc".into(),
            side: "sell".into(),
            price: "36.50".into(),
            quantity: None,
        })
        .is_err());
    }

    #[test]
    fn rejected_order_is_not_accepted() {
        let mut order = sample("rejected", Some("sem saldo"));
        let error = accept_order(order.clone()).unwrap_err();
        assert_eq!(error.kind, ApiErrorKind::Rejected);
        assert_eq!(error.title, "sem saldo");
        assert_eq!(error.code.as_deref(), Some("sem saldo"));
        order.status = "accepted".into();
        assert!(accept_order(order).is_ok());

        let nested = sample("rejected", None);
        let action = accept_action(PositionAction {
            status: "sent".into(),
            order: Some(nested),
        })
        .unwrap_err();
        assert_eq!(action.kind, ApiErrorKind::Rejected);
        assert_eq!(action.title, "A ordem foi recusada.");
    }

    fn sample(status: &str, reason: Option<&str>) -> Order {
        serde_json::from_str(&format!(
            r#"{{
                "orderId": "11111111-1111-1111-1111-111111111111",
                "instrumentId": "11111111-1111-1111-1111-111111111111",
                "side": "buy",
                "type": "market",
                "origin": "session",
                "quantity": "100",
                "limitPrice": null,
                "timeInForce": "day",
                "status": "{status}",
                "filledQuantity": "0",
                "remainingQuantity": "100",
                "rejectionReason": {},
                "createdAt": "2026-10-14T13:00:00Z",
                "lastUpdatedAt": "2026-10-14T13:00:00Z",
                "completedAt": null,
                "fills": []
            }}"#,
            reason
                .map(|item| format!("\"{item}\""))
                .unwrap_or_else(|| "null".into())
        ))
        .unwrap()
    }
}
