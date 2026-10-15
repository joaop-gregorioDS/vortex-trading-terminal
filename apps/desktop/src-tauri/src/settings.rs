use tauri::{Theme as WindowTheme, WebviewWindow};
use tauri_plugin_store::StoreExt;

use crate::dto::Theme;
use crate::error::ApiError;

const STORE_FILE: &str = "desktop-settings.json";
const THEME_KEY: &str = "theme";

pub fn read_theme(app: &tauri::AppHandle) -> Theme {
    let Ok(store) = app.store(STORE_FILE) else {
        return Theme::System;
    };
    store
        .get(THEME_KEY)
        .and_then(|value| serde_json::to_value(value).ok())
        .and_then(|value| value.as_str().and_then(Theme::parse))
        .unwrap_or(Theme::System)
}

pub fn write_theme(app: &tauri::AppHandle, theme: Theme) -> Result<(), ApiError> {
    let store = app.store(STORE_FILE).map_err(|error| {
        tracing::warn!(%error, "store");
        ApiError::contract("Não foi possível gravar o tema.")
    })?;
    store.set(THEME_KEY, serde_json::json!(theme.as_str()));
    store.save().map_err(|error| {
        tracing::warn!(%error, "store");
        ApiError::contract("Não foi possível gravar o tema.")
    })?;
    Ok(())
}

pub fn apply_window_theme(window: &WebviewWindow, theme: Theme) {
    let native = match theme {
        Theme::Light => Some(WindowTheme::Light),
        Theme::Dark => Some(WindowTheme::Dark),
        Theme::System => None,
    };
    if let Err(error) = window.set_theme(native) {
        tracing::warn!(%error, "tema da janela");
    }
}

#[cfg(test)]
mod tests {
    use super::Theme;

    #[test]
    fn theme_names_are_the_three_modes() {
        assert_eq!(Theme::parse("light"), Some(Theme::Light));
        assert_eq!(Theme::parse("dark"), Some(Theme::Dark));
        assert_eq!(Theme::parse("system"), Some(Theme::System));
        assert_eq!(Theme::parse("Light"), None);
        assert_eq!(Theme::parse("azul"), None);
        assert_eq!(Theme::Dark.as_str(), "dark");
        let stored: Theme = serde_json::from_str("\"system\"").unwrap();
        assert_eq!(stored, Theme::System);
        assert!(serde_json::from_str::<Theme>("\"azul\"").is_err());
    }
}
