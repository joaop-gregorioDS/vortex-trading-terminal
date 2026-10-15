use std::collections::BTreeSet;

use crate::dto::{FillNotice, TerminalSnapshot};
use crate::format_money::group_decimal;

/// Ids já vistos. O primeiro snapshot só grava. Os seguintes emitem o que for novo.
#[derive(Default)]
pub struct SeenExecutions {
    primed: bool,
    ids: BTreeSet<String>,
}

pub fn new_fills(seen: &mut SeenExecutions, snapshot: &TerminalSnapshot) -> Vec<FillNotice> {
    let mut notices = Vec::new();
    for order in &snapshot.orders {
        let ticker = snapshot
            .quotes
            .iter()
            .find(|quote| quote.instrument_id == order.instrument_id)
            .map(|quote| quote.ticker.clone())
            .unwrap_or_default();
        for fill in &order.fills {
            let id = fill.execution_id.trim();
            if id.is_empty() || !seen.ids.insert(id.to_owned()) {
                continue;
            }
            if !seen.primed {
                continue;
            }
            notices.push(FillNotice {
                order_id: order.order_id.clone(),
                ticker: ticker.clone(),
                side: order.side.clone(),
                quantity: fill.quantity.clone(),
                price: fill.price.clone(),
                executed_at: fill.executed_at.clone(),
            });
        }
    }
    seen.primed = true;
    notices
}

/// Texto da notificação. O lado desconhecido fica como a API mandou.
pub fn fill_body(notice: &FillNotice) -> String {
    let side = match notice.side.as_str() {
        "buy" => "compra",
        "sell" => "venda",
        other => other,
    };
    let quantity = grouped(notice.quantity.as_str());
    let price = grouped(notice.price.as_str());
    if notice.ticker.is_empty() {
        format!("{side} · {quantity} · {price}")
    } else {
        format!("{} · {side} · {quantity} · {price}", notice.ticker)
    }
}

fn grouped(value: &str) -> String {
    group_decimal(value).unwrap_or_else(|_| value.to_owned())
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::dto::{DecimalString, Fill, MarketQuote, Order};

    fn money(value: &str) -> DecimalString {
        DecimalString::parse(value).unwrap()
    }

    fn fill(id: &str, quantity: &str, price: &str) -> Fill {
        Fill {
            execution_id: id.into(),
            quantity: money(quantity),
            price: money(price),
            executed_at: "2026-10-14T13:00:01Z".into(),
        }
    }

    fn order(id: &str, instrument: &str, side: &str, fills: Vec<Fill>) -> Order {
        Order {
            order_id: id.into(),
            instrument_id: instrument.into(),
            side: side.into(),
            r#type: "market".into(),
            origin: "session".into(),
            quantity: money("1"),
            limit_price: None,
            time_in_force: "day".into(),
            status: "filled".into(),
            filled_quantity: money("1"),
            remaining_quantity: money("0"),
            rejection_reason: None,
            created_at: "2026-10-14T13:00:00Z".into(),
            last_updated_at: "2026-10-14T13:00:01Z".into(),
            completed_at: None,
            fills,
            role: None,
        }
    }

    fn snapshot(orders: Vec<Order>) -> TerminalSnapshot {
        let mut body: TerminalSnapshot =
            serde_json::from_str(include_str!("../tests/fixtures/terminal.json")).unwrap();
        body.orders = orders;
        body
    }

    #[test]
    fn first_snapshot_is_silent_and_only_a_new_id_is_emitted() {
        let mut seen = SeenExecutions::default();
        let instrument = "11111111-1111-1111-1111-111111111111";
        let first = snapshot(vec![order(
            "o1",
            instrument,
            "buy",
            vec![fill("e1", "10.50", "36.5")],
        )]);
        assert!(new_fills(&mut seen, &first).is_empty());

        let second = snapshot(vec![order(
            "o1",
            instrument,
            "buy",
            vec![
                fill("e1", "10.50", "36.5"),
                fill("e2", "2.5", "36.25"),
                fill("  ", "1", "1"),
                fill("", "1", "1"),
            ],
        )]);
        let notices = new_fills(&mut seen, &second);
        assert_eq!(notices.len(), 1);
        assert_eq!(notices[0].order_id, "o1");
        assert_eq!(notices[0].ticker, "PETR4");
        assert_eq!(notices[0].side, "buy");
        assert_eq!(notices[0].quantity.as_str(), "2.5");
        assert_eq!(notices[0].price.as_str(), "36.25");
        assert_eq!(notices[0].executed_at, "2026-10-14T13:00:01Z");
    }

    #[test]
    fn missing_quote_keeps_the_side_and_an_empty_ticker() {
        let mut seen = SeenExecutions::default();
        assert!(new_fills(&mut seen, &snapshot(vec![])).is_empty());
        let next = snapshot(vec![order(
            "o2",
            "22222222-2222-2222-2222-222222222222",
            "cover",
            vec![fill("e9", "3", "10")],
        )]);
        let notices = new_fills(&mut seen, &next);
        assert_eq!(notices.len(), 1);
        assert_eq!(notices[0].ticker, "");
        assert_eq!(notices[0].side, "cover");
        assert_eq!(notices[0].quantity.as_str(), "3");
        assert_eq!(notices[0].price.as_str(), "10");
        assert_eq!(fill_body(&notices[0]), "cover · 3 · 10");
    }

    #[test]
    fn notice_text_groups_the_decimal_and_names_the_side() {
        let notice = FillNotice {
            order_id: "o".into(),
            ticker: "PETR4".into(),
            side: "sell".into(),
            quantity: money("1500.5"),
            price: money("36.50"),
            executed_at: "2026-10-14T13:00:01Z".into(),
        };
        assert_eq!(fill_body(&notice), "PETR4 · venda · 1.500,5 · 36,50");
        let buy = FillNotice {
            side: "buy".into(),
            ticker: String::new(),
            ..notice
        };
        assert_eq!(fill_body(&buy), "compra · 1.500,5 · 36,50");
    }

    #[test]
    fn quote_lookup_uses_the_instrument_on_the_snapshot() {
        let mut seen = SeenExecutions::default();
        let _ = new_fills(&mut seen, &snapshot(vec![]));
        let mut body = snapshot(vec![order(
            "o3",
            "33333333-3333-3333-3333-333333333333",
            "buy",
            vec![fill("e3", "1", "1")],
        )]);
        body.quotes.push(MarketQuote {
            ticker: "VALE3".into(),
            name: "Vale".into(),
            category: "equity".into(),
            instrument_id: "33333333-3333-3333-3333-333333333333".into(),
            last_price: money("60"),
            change_percent: money("0"),
            change_points: money("0"),
            min: money("60"),
            max: money("60"),
            volume: money("1"),
            updated_at: "2026-10-14T13:00:00Z".into(),
        });
        let notices = new_fills(&mut seen, &body);
        assert_eq!(notices[0].ticker, "VALE3");
        assert_eq!(notices[0].side, "buy");
    }
}
