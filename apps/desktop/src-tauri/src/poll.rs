use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};

use crate::client::ApiClient;
use crate::dto::{ConnectionState, ConnectionStatus, FillNotice, TerminalSnapshot};
use crate::fills::{new_fills, SeenExecutions};
use crate::format_money::group_decimal;

#[derive(Clone, Debug)]
pub struct IntervalInput {
    pub focused: bool,
    pub minimized: bool,
    pub session_open: Option<bool>,
    pub failures: u32,
}

pub fn poll_interval(input: IntervalInput, entropy: u64) -> Duration {
    if input.failures > 0 {
        return Duration::from_millis(backoff_ms(input.failures, entropy));
    }
    if input.session_open == Some(false) {
        return Duration::from_secs(30);
    }
    if !input.focused || input.minimized || input.session_open.is_none() {
        return Duration::from_secs(5);
    }
    Duration::from_millis(1500)
}

pub fn backoff_ms(failures: u32, entropy: u64) -> u64 {
    let shift = failures.saturating_sub(1).min(6);
    let base = (1_000u64 << shift).min(60_000);
    let spread = base / 5;
    let delta = entropy % (spread * 2);
    (base - spread + delta).min(60_000)
}

pub fn should_apply(latest_applied: u64, incoming: u64) -> bool {
    incoming > latest_applied
}

pub fn tray_tooltip(state: &ConnectionState, pnl: Option<&str>) -> String {
    if state.state == ConnectionStatus::Disconnected {
        return "Sem conexão".to_owned();
    }
    let session = match state.session_open {
        Some(true) => "Pregão aberto",
        Some(false) => "Pregão fechado",
        None => "Aguardando pregão",
    };
    let prefix = if state.state == ConnectionStatus::Degraded {
        "Desatualizado · "
    } else {
        ""
    };
    match pnl {
        Some(value) => format!("{prefix}{session} · P&L {value}"),
        None => format!("{prefix}{session}"),
    }
}

pub enum PollUpdate {
    Snapshot {
        snapshot: TerminalSnapshot,
        connection: ConnectionState,
        tooltip: String,
        fills: Vec<FillNotice>,
    },
    Connection {
        connection: ConnectionState,
        tooltip: String,
    },
}

pub async fn poll_loop<F, G>(
    client: ApiClient,
    focused: Arc<AtomicBool>,
    mut minimized: G,
    mut emit: F,
) where
    F: FnMut(PollUpdate),
    G: FnMut() -> bool,
{
    let mut applied = 0u64;
    let mut generation = 0u64;
    let mut failures = 0u32;
    let mut session_open = None;
    let mut last_success_at = None;
    let mut last_pnl: Option<String> = None;
    let mut seen = SeenExecutions::default();

    loop {
        generation = generation.saturating_add(1);
        let incoming = generation;
        let started = Instant::now();
        let result = client.terminal_once().await;
        if !should_apply(applied, incoming) {
            continue;
        }
        applied = incoming;
        let input = IntervalInput {
            focused: focused.load(Ordering::Relaxed),
            minimized: minimized(),
            session_open,
            failures,
        };
        match result {
            Ok(snapshot) => {
                failures = 0;
                session_open = snapshot.session_open;
                last_success_at = Some(utc_now());
                last_pnl = group_decimal(snapshot.portfolio.total_profit_and_loss.as_str()).ok();
                let interval = poll_interval(
                    IntervalInput {
                        focused: input.focused,
                        minimized: input.minimized,
                        session_open,
                        failures,
                    },
                    timer_entropy(),
                );
                let connection = ConnectionState {
                    state: ConnectionStatus::Connected,
                    session_open,
                    last_success_at: last_success_at.clone(),
                    retry_in_ms: Some(millis(interval)),
                };
                let tooltip = tray_tooltip(&connection, last_pnl.as_deref());
                let fills = new_fills(&mut seen, &snapshot);
                emit(PollUpdate::Snapshot {
                    snapshot,
                    connection,
                    tooltip,
                    fills,
                });
                sleep_rest(started, interval).await;
            }
            Err(error) => {
                tracing::debug!(kind = ?error.kind, "poll");
                failures = failures.saturating_add(1);
                let interval = poll_interval(IntervalInput { failures, ..input }, timer_entropy());
                let state = if failures >= 3 {
                    ConnectionStatus::Disconnected
                } else {
                    ConnectionStatus::Degraded
                };
                let connection = ConnectionState {
                    state,
                    session_open,
                    last_success_at: last_success_at.clone(),
                    retry_in_ms: Some(millis(interval)),
                };
                let pnl = if state == ConnectionStatus::Disconnected {
                    None
                } else {
                    last_pnl.as_deref()
                };
                let tooltip = tray_tooltip(&connection, pnl);
                emit(PollUpdate::Connection {
                    connection,
                    tooltip,
                });
                sleep_rest(started, interval).await;
            }
        }
    }
}

async fn sleep_rest(started: Instant, interval: Duration) {
    tokio::time::sleep(interval.saturating_sub(started.elapsed())).await;
}

fn millis(interval: Duration) -> u64 {
    u64::try_from(interval.as_millis()).unwrap_or(u64::MAX)
}

fn timer_entropy() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|duration| duration.subsec_nanos() as u64)
        .unwrap_or(0)
}

fn utc_now() -> String {
    let seconds = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_secs();
    format_unix_seconds(seconds)
}

pub fn format_unix_seconds(seconds: u64) -> String {
    let days = (seconds / 86_400) as i64;
    let time_of_day = seconds % 86_400;
    let (year, month, day) = civil_from_days(days);
    let hour = time_of_day / 3_600;
    let minute = (time_of_day % 3_600) / 60;
    let second = time_of_day % 60;
    format!("{year:04}-{month:02}-{day:02}T{hour:02}:{minute:02}:{second:02}Z")
}

fn civil_from_days(days_since_epoch: i64) -> (i32, u32, u32) {
    let z = days_since_epoch + 719_468;
    let era = if z >= 0 { z } else { z - 146_096 } / 146_097;
    let day_of_era = (z - era * 146_097) as u64;
    let year_of_era =
        (day_of_era - day_of_era / 1_460 + day_of_era / 36_524 - day_of_era / 146_096) / 365;
    let year = year_of_era as i64 + era * 400;
    let day_of_year = day_of_era - (365 * year_of_era + year_of_era / 4 - year_of_era / 100);
    let month_part = (5 * day_of_year + 2) / 153;
    let day = day_of_year - (153 * month_part + 2) / 5 + 1;
    let month = if month_part < 10 {
        month_part + 3
    } else {
        month_part - 9
    };
    let year = if month <= 2 { year + 1 } else { year };
    (year as i32, month as u32, day as u32)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn input(
        focused: bool,
        minimized: bool,
        session_open: Option<bool>,
        failures: u32,
    ) -> IntervalInput {
        IntervalInput {
            focused,
            minimized,
            session_open,
            failures,
        }
    }

    #[test]
    fn interval_priority() {
        assert_eq!(
            poll_interval(input(true, false, Some(true), 0), 0),
            Duration::from_millis(1500)
        );
        assert_eq!(
            poll_interval(input(false, false, Some(true), 0), 0),
            Duration::from_secs(5)
        );
        assert_eq!(
            poll_interval(input(true, true, Some(true), 0), 0),
            Duration::from_secs(5)
        );
        assert_eq!(
            poll_interval(input(true, false, None, 0), 0),
            Duration::from_secs(5)
        );
        assert_eq!(
            poll_interval(input(false, false, Some(false), 0), 0),
            Duration::from_secs(30)
        );
        assert_eq!(
            poll_interval(input(true, false, Some(false), 1), 0),
            Duration::from_millis(800)
        );
    }

    #[test]
    fn backoff_stays_inside_the_cap() {
        assert_eq!(backoff_ms(1, 0), 800);
        assert_eq!(backoff_ms(2, 0), 1_600);
        assert_eq!(backoff_ms(3, 0), 3_200);
        for entropy in [0_u64, 1, 199, 399, 400, 10_000, u64::MAX] {
            let value = backoff_ms(1, entropy);
            assert!((800..=1_200).contains(&value), "{value}");
            assert!(backoff_ms(20, entropy) <= 60_000);
        }
        assert!((48_000..=60_000).contains(&backoff_ms(20, 0)));
    }

    #[test]
    fn older_generation_does_not_replace_the_newer_one() {
        let mut latest = 0;
        assert!(should_apply(latest, 2));
        latest = 2;
        assert!(!should_apply(latest, 1));
        assert!(should_apply(latest, 3));
    }

    #[test]
    fn tray_hides_money_when_disconnected() {
        let open = ConnectionState {
            state: ConnectionStatus::Connected,
            session_open: Some(true),
            last_success_at: None,
            retry_in_ms: Some(1500),
        };
        assert_eq!(
            tray_tooltip(&open, Some("1.000,00")),
            "Pregão aberto · P&L 1.000,00"
        );
        let stale = ConnectionState {
            state: ConnectionStatus::Degraded,
            ..open.clone()
        };
        assert_eq!(
            tray_tooltip(&stale, Some("1.000,00")),
            "Desatualizado · Pregão aberto · P&L 1.000,00"
        );
        let down = ConnectionState {
            state: ConnectionStatus::Disconnected,
            ..open
        };
        assert_eq!(tray_tooltip(&down, Some("1.000,00")), "Sem conexão");
        let closed = ConnectionState {
            state: ConnectionStatus::Connected,
            session_open: Some(false),
            last_success_at: None,
            retry_in_ms: Some(30_000),
        };
        assert_eq!(
            tray_tooltip(&closed, Some("1.000,00")),
            "Pregão fechado · P&L 1.000,00"
        );
    }

    #[test]
    fn unix_dates() {
        assert_eq!(format_unix_seconds(0), "1970-01-01T00:00:00Z");
        assert_eq!(format_unix_seconds(86_400), "1970-01-02T00:00:00Z");
        assert_eq!(format_unix_seconds(1_704_067_200), "2024-01-01T00:00:00Z");
    }
}
