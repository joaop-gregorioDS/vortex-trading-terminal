use tauri::menu::{Menu, MenuItem, PredefinedMenuItem, Submenu};
use tauri::{AppHandle, Emitter, Manager};

use crate::commands::{assign_theme, AppState};
use crate::danger;
use crate::dto::{DangerAction, DiagnosticReport, HealthText, Theme};
use crate::error::{ApiError, ApiErrorKind};

pub const TRAY_ACTIONS: &[(&str, &str)] = &[
    ("show", "Abrir Vortex Desktop"),
    ("theme-light", "Tema claro"),
    ("theme-dark", "Tema escuro"),
    ("theme-system", "Tema do sistema"),
    ("health-live", "Saúde da API"),
    ("health-ready", "Prontidão da API"),
    ("api-status", "Estado da sessão"),
];

pub struct StationItem {
    pub id: &'static str,
    pub label: &'static str,
    pub accelerator: Option<&'static str>,
}

pub const FILE_MENU: &[StationItem] = &[StationItem {
    id: "export-trades",
    label: "Exportar negócios",
    accelerator: Some("CmdOrCtrl+Shift+E"),
}];

pub const VIEW_MENU: &[StationItem] = &[
    StationItem {
        id: "view-overview",
        label: "Visão Geral",
        accelerator: Some("CmdOrCtrl+1"),
    },
    StationItem {
        id: "view-desk",
        label: "Mesa",
        accelerator: Some("CmdOrCtrl+2"),
    },
    StationItem {
        id: "view-reports",
        label: "Relatórios",
        accelerator: Some("CmdOrCtrl+3"),
    },
    StationItem {
        id: "theme-light",
        label: "Tema claro",
        accelerator: None,
    },
    StationItem {
        id: "theme-dark",
        label: "Tema escuro",
        accelerator: None,
    },
    StationItem {
        id: "theme-system",
        label: "Tema do sistema",
        accelerator: None,
    },
];

pub const OPERATE_MENU: &[StationItem] = &[
    StationItem {
        id: "danger-reset",
        label: "Resetar a sessão",
        accelerator: Some("CmdOrCtrl+Shift+R"),
    },
    StationItem {
        id: "danger-emergency-flat",
        label: "Zerar tudo",
        accelerator: Some("CmdOrCtrl+Shift+0"),
    },
    StationItem {
        id: "danger-reverse",
        label: "Inverter a posição",
        accelerator: Some("CmdOrCtrl+Shift+I"),
    },
];

pub const HELP_MENU: &[StationItem] = &[
    StationItem {
        id: "health-live",
        label: "Saúde da API",
        accelerator: None,
    },
    StationItem {
        id: "health-ready",
        label: "Prontidão da API",
        accelerator: None,
    },
    StationItem {
        id: "api-status",
        label: "Estado da sessão",
        accelerator: None,
    },
];

const MENU_NOTICE: &str = "A confirmação não enviou a ação. Use a boleta.";

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum MenuEffect {
    Open,
    Theme(Theme),
    Probe,
    View,
    Export,
    Danger(DangerAction),
    None,
}

pub fn menu_effect(id: &str) -> MenuEffect {
    match id {
        "show" => MenuEffect::Open,
        "theme-light" => MenuEffect::Theme(Theme::Light),
        "theme-dark" => MenuEffect::Theme(Theme::Dark),
        "theme-system" => MenuEffect::Theme(Theme::System),
        "health-live" | "health-ready" | "api-status" => MenuEffect::Probe,
        "view-overview" | "view-desk" | "view-reports" => MenuEffect::View,
        "export-trades" => MenuEffect::Export,
        "danger-reset" => MenuEffect::Danger(DangerAction::Reset),
        "danger-emergency-flat" => MenuEffect::Danger(DangerAction::EmergencyFlat),
        "danger-reverse" => MenuEffect::Danger(DangerAction::Reverse),
        _ => MenuEffect::None,
    }
}

enum Probe {
    Live,
    Ready,
    Status,
}

pub fn tray_menu(app: &AppHandle) -> tauri::Result<Menu<tauri::Wry>> {
    let show = item(app, "show")?;
    let theme = Submenu::with_items(
        app,
        "Tema",
        true,
        &[
            &item(app, "theme-light")?,
            &item(app, "theme-dark")?,
            &item(app, "theme-system")?,
        ],
    )?;
    let diagnostic = Submenu::with_items(
        app,
        "Diagnóstico",
        true,
        &[
            &item(app, "health-live")?,
            &item(app, "health-ready")?,
            &item(app, "api-status")?,
        ],
    )?;
    Menu::with_items(app, &[&show, &theme, &diagnostic])
}

pub fn install_app_menu(app: &tauri::App) -> tauri::Result<()> {
    #[cfg(target_os = "macos")]
    {
        let menu = macos_menu(app.handle())?;
        app.set_menu(menu)?;
        app.on_menu_event(|app, event| dispatch(app, event.id.as_ref()));
    }
    #[cfg(not(target_os = "macos"))]
    let _ = app;
    Ok(())
}

pub fn dispatch(app: &AppHandle, id: &str) {
    match menu_effect(id) {
        MenuEffect::Open => open_main(app),
        MenuEffect::Theme(theme) => {
            let _ = assign_theme(app, theme);
        }
        MenuEffect::Probe => probe_id(app, id),
        MenuEffect::View | MenuEffect::Export => {
            let _ = app.emit("desktop://menu", id);
        }
        MenuEffect::Danger(action) => ask_without_sending(app, action),
        MenuEffect::None => {}
    }
}

pub fn open_main(app: &AppHandle) {
    if let Some(window) = app.get_webview_window("main") {
        let _ = window.unminimize();
        let _ = window.show();
        let _ = window.set_focus();
    }
}

fn ask_without_sending(app: &AppHandle, action: DangerAction) {
    let app = app.clone();
    let gate = app.state::<AppState>().danger.clone();
    tauri::async_runtime::spawn(async move {
        match danger::confirm(&gate, action, app.clone()).await {
            Ok(token) => {
                let _ = gate.take(action, &token.token);
                let _ = app.emit("desktop://notice", MENU_NOTICE);
            }
            Err(error) if error.kind == ApiErrorKind::Cancelled => {}
            Err(error) => {
                let _ = app.emit("desktop://notice", error.title);
            }
        }
    });
}

fn item(app: &AppHandle, id: &str) -> tauri::Result<MenuItem<tauri::Wry>> {
    let label = TRAY_ACTIONS
        .iter()
        .find(|(action, _)| *action == id)
        .map(|(_, label)| *label)
        .unwrap_or(id);
    MenuItem::with_id(app, id, label, true, None::<&str>)
}

#[cfg_attr(not(target_os = "macos"), allow(dead_code))]
fn station_item(app: &AppHandle, entry: &StationItem) -> tauri::Result<MenuItem<tauri::Wry>> {
    MenuItem::with_id(app, entry.id, entry.label, true, entry.accelerator)
}

#[cfg_attr(not(target_os = "macos"), allow(dead_code))]
fn station_submenu(
    app: &AppHandle,
    title: &str,
    entries: &[StationItem],
) -> tauri::Result<Submenu<tauri::Wry>> {
    let menu = Submenu::with_id(app, title, title, true)?;
    for entry in entries {
        menu.append(&station_item(app, entry)?)?;
    }
    Ok(menu)
}

#[cfg_attr(not(target_os = "macos"), allow(dead_code))]
fn view_menu(app: &AppHandle) -> tauri::Result<Submenu<tauri::Wry>> {
    let menu = Submenu::with_id(app, "Exibir", "Exibir", true)?;
    let mut themes = false;
    for entry in VIEW_MENU {
        if !themes && entry.id.starts_with("theme-") {
            menu.append(&PredefinedMenuItem::separator(app)?)?;
            themes = true;
        }
        menu.append(&station_item(app, entry)?)?;
    }
    Ok(menu)
}

#[cfg_attr(not(target_os = "macos"), allow(dead_code))]
fn macos_menu(app: &AppHandle) -> tauri::Result<Menu<tauri::Wry>> {
    let app_menu = Submenu::with_items(
        app,
        "Vortex Desktop",
        true,
        &[
            &PredefinedMenuItem::about(app, Some("Sobre o Vortex Desktop"), None)?,
            &PredefinedMenuItem::separator(app)?,
            &PredefinedMenuItem::hide(app, None)?,
            &PredefinedMenuItem::hide_others(app, None)?,
            &PredefinedMenuItem::separator(app)?,
            &PredefinedMenuItem::quit(app, None)?,
        ],
    )?;
    let edit = Submenu::with_items(
        app,
        "Editar",
        true,
        &[
            &PredefinedMenuItem::undo(app, None)?,
            &PredefinedMenuItem::redo(app, None)?,
            &PredefinedMenuItem::separator(app)?,
            &PredefinedMenuItem::cut(app, None)?,
            &PredefinedMenuItem::copy(app, None)?,
            &PredefinedMenuItem::paste(app, None)?,
            &PredefinedMenuItem::select_all(app, None)?,
        ],
    )?;
    let file = station_submenu(app, "Arquivo", FILE_MENU)?;
    let view = view_menu(app)?;
    let operate = station_submenu(app, "Operar", OPERATE_MENU)?;
    let help = station_submenu(app, "Ajuda", HELP_MENU)?;
    Menu::with_items(app, &[&app_menu, &file, &edit, &view, &operate, &help])
}

fn probe_id(app: &AppHandle, id: &str) {
    let kind = match id {
        "health-live" => Probe::Live,
        "health-ready" => Probe::Ready,
        "api-status" => Probe::Status,
        _ => return,
    };
    probe(app, kind);
}

fn probe(app: &AppHandle, kind: Probe) {
    let app = app.clone();
    let client = app.state::<AppState>().client.clone();
    tauri::async_runtime::spawn(async move {
        let report = match client {
            Err(error) => failed(error),
            Ok(client) => match kind {
                Probe::Live => health_report("Saúde da API", client.health("health/live").await),
                Probe::Ready => {
                    health_report("Prontidão da API", client.health("health/ready").await)
                }
                Probe::Status => match client.status().await {
                    Ok(status) => DiagnosticReport {
                        title: "Estado da sessão".into(),
                        health: None,
                        status: Some(status),
                    },
                    Err(error) => failed(error),
                },
            },
        };
        let _ = app.emit("desktop://diagnostic", report);
    });
}

fn health_report(title: &str, result: Result<HealthText, ApiError>) -> DiagnosticReport {
    match result {
        Ok(health) => DiagnosticReport {
            title: title.to_owned(),
            health: Some(health),
            status: None,
        },
        Err(error) => failed(error),
    }
}

fn failed(error: ApiError) -> DiagnosticReport {
    DiagnosticReport {
        title: error.title,
        health: None,
        status: None,
    }
}

#[cfg(test)]
mod tests {
    use super::{
        menu_effect, MenuEffect, StationItem, FILE_MENU, HELP_MENU, OPERATE_MENU, TRAY_ACTIONS,
        VIEW_MENU,
    };
    use crate::dto::DangerAction;

    fn accelerators(entries: &[StationItem]) -> Vec<&str> {
        entries
            .iter()
            .filter_map(|entry| entry.accelerator)
            .collect()
    }

    #[test]
    fn diagnostic_menu_lists_health_status_and_themes() {
        let ids: Vec<_> = TRAY_ACTIONS.iter().map(|(id, _)| *id).collect();
        assert!(ids.contains(&"health-live"));
        assert!(ids.contains(&"health-ready"));
        assert!(ids.contains(&"api-status"));
        assert!(ids.contains(&"theme-light"));
        assert!(ids.contains(&"theme-dark"));
        assert!(ids.contains(&"theme-system"));
        assert!(TRAY_ACTIONS
            .iter()
            .any(|(_, label)| *label == "Saúde da API"));
        assert!(TRAY_ACTIONS
            .iter()
            .any(|(_, label)| *label == "Estado da sessão"));
    }

    #[test]
    fn operate_opens_the_dialog_and_does_not_name_the_commands() {
        assert_eq!(
            menu_effect("danger-reset"),
            MenuEffect::Danger(DangerAction::Reset)
        );
        assert_eq!(
            menu_effect("danger-emergency-flat"),
            MenuEffect::Danger(DangerAction::EmergencyFlat)
        );
        assert_eq!(
            menu_effect("danger-reverse"),
            MenuEffect::Danger(DangerAction::Reverse)
        );
        let forbidden = ["reset_session", "emergency_flat", "reverse", "reset"];
        for entry in OPERATE_MENU {
            assert!(!forbidden.contains(&entry.id), "{}", entry.id);
            assert!(matches!(menu_effect(entry.id), MenuEffect::Danger(_)));
        }
        assert_eq!(menu_effect("export-trades"), MenuEffect::Export);
        assert_eq!(menu_effect("view-desk"), MenuEffect::View);
        assert_eq!(menu_effect("view-overview"), MenuEffect::View);
        assert_eq!(menu_effect("view-reports"), MenuEffect::View);
    }

    #[test]
    fn accelerators_stay_on_the_menu_and_use_the_window_modifier() {
        let station = accelerators(FILE_MENU)
            .into_iter()
            .chain(accelerators(VIEW_MENU))
            .chain(accelerators(OPERATE_MENU))
            .chain(accelerators(HELP_MENU));
        let mut seen = 0;
        for accelerator in station {
            assert!(accelerator.starts_with("CmdOrCtrl+"), "{accelerator}");
            seen += 1;
        }
        assert!(seen > 0);
        assert!(TRAY_ACTIONS
            .iter()
            .all(|(id, _)| menu_effect(id) != MenuEffect::None));
    }
}
