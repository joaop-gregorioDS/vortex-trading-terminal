mod client;
mod commands;
mod danger;
mod dto;
mod error;
mod fills;
mod format_money;
mod menu;
mod orders;
mod poll;
mod reports;
mod settings;

#[cfg(test)]
mod bindings;

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;

use tauri::tray::{MouseButton, MouseButtonState, TrayIconBuilder, TrayIconEvent};
use tauri::{Emitter, Manager, WindowEvent};
use tauri_plugin_notification::NotificationExt;

use crate::commands::{
    get_chart, get_config, get_history, get_status, get_terminal, health_live, health_ready,
    set_theme, AppState,
};
use crate::danger::DangerGate;
use crate::orders::{
    cancel_order, chart_preview, confirm_danger, emergency_flat, flatten, place_protection,
    reset_session, reverse, revise_order, submit_order,
};
use crate::poll::{poll_loop, PollUpdate};
use crate::reports::{export_trades_csv, get_performance, get_trades};
use crate::settings::{apply_window_theme, read_theme};

pub fn run() {
    let filter = tracing_subscriber::EnvFilter::try_from_default_env()
        .unwrap_or_else(|_| tracing_subscriber::EnvFilter::new("vortex_desktop=info"));
    tracing_subscriber::fmt()
        .with_writer(std::io::stderr)
        .with_env_filter(filter)
        .init();

    let client = client::ApiClient::from_env();
    match &client {
        Ok(client) => {
            let config = client.config();
            tracing::info!(scheme = %config.scheme, host = %config.host, "api");
        }
        Err(error) => tracing::warn!(%error, "url base rejeitada"),
    }

    tauri::Builder::default()
        .plugin(tauri_plugin_single_instance::init(|app, _argv, _cwd| {
            let app = app.clone();
            let window = app.clone();
            let _ = app.run_on_main_thread(move || menu::open_main(&window));
        }))
        .plugin(
            tauri_plugin_window_state::Builder::new()
                .with_state_flags(
                    tauri_plugin_window_state::StateFlags::SIZE
                        | tauri_plugin_window_state::StateFlags::POSITION
                        | tauri_plugin_window_state::StateFlags::MAXIMIZED,
                )
                .build(),
        )
        .plugin(tauri_plugin_store::Builder::default().build())
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_notification::init())
        .manage(AppState {
            client: client.clone(),
            danger: DangerGate::new(),
        })
        .setup(move |app| {
            let notify = app.handle().clone();
            if let Err(error) = app.notification().on_action(move |_action| {
                let window = notify.clone();
                let _ = notify.run_on_main_thread(move || menu::open_main(&window));
            }) {
                tracing::debug!(?error, "clique da notificação");
            }
            let focused = Arc::new(AtomicBool::new(true));
            let focus_flag = focused.clone();
            let window = app
                .get_webview_window("main")
                .ok_or("A janela main não foi criada.")?;
            apply_window_theme(&window, read_theme(app.handle()));
            window.on_window_event(move |event| {
                if let WindowEvent::Focused(value) = event {
                    focus_flag.store(*value, Ordering::Relaxed);
                }
            });
            menu::install_app_menu(app)?;

            let icon = app
                .default_window_icon()
                .cloned()
                .ok_or("O ícone da janela não foi carregado.")?;
            let menu = menu::tray_menu(app.handle())?;
            let tray = TrayIconBuilder::with_id("status")
                .icon(icon)
                .tooltip(if client.is_ok() {
                    "Vortex Desktop"
                } else {
                    "Sem conexão"
                })
                .menu(&menu)
                .on_menu_event(|app, event| menu::dispatch(app, event.id.as_ref()))
                .on_tray_icon_event(|tray, event| {
                    if let TrayIconEvent::Click {
                        button: MouseButton::Left,
                        button_state: MouseButtonState::Up,
                        ..
                    } = event
                    {
                        menu::open_main(tray.app_handle());
                    }
                })
                .build(app)?;

            if let Ok(client) = client {
                let handle = app.handle().clone();
                let watch_focus = focused.clone();
                tauri::async_runtime::spawn(async move {
                    let minimized = {
                        let handle = handle.clone();
                        move || {
                            handle
                                .get_webview_window("main")
                                .and_then(|window| window.is_minimized().ok())
                                .unwrap_or(false)
                        }
                    };
                    poll_loop(client, watch_focus, minimized, move |update| {
                        apply_update(&handle, &tray, update);
                    })
                    .await;
                });
            }
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            get_terminal,
            get_chart,
            get_history,
            get_config,
            set_theme,
            health_live,
            health_ready,
            get_status,
            submit_order,
            chart_preview,
            revise_order,
            cancel_order,
            place_protection,
            flatten,
            reverse,
            emergency_flat,
            reset_session,
            confirm_danger,
            get_performance,
            get_trades,
            export_trades_csv
        ])
        .run(tauri::generate_context!())
        .expect("Vortex Desktop encerrou");
}

fn show_fill(app: &tauri::AppHandle, notice: &crate::dto::FillNotice) {
    if let Err(error) = app
        .notification()
        .builder()
        .title("Execução")
        .body(crate::fills::fill_body(notice))
        .show()
    {
        tracing::debug!(?error, "notificação de execução");
    }
}

fn apply_update(app: &tauri::AppHandle, tray: &tauri::tray::TrayIcon, update: PollUpdate) {
    match update {
        PollUpdate::Snapshot {
            snapshot,
            connection,
            tooltip,
            fills,
        } => {
            let _ = app.emit("terminal://snapshot", snapshot);
            let _ = app.emit("terminal://connection", connection);
            let _ = tray.set_tooltip(Some(tooltip));
            for notice in fills {
                let _ = app.emit("orders://fill", &notice);
                show_fill(app, &notice);
            }
        }
        PollUpdate::Connection {
            connection,
            tooltip,
        } => {
            let _ = app.emit("terminal://connection", connection);
            let _ = tray.set_tooltip(Some(tooltip));
        }
    }
}
