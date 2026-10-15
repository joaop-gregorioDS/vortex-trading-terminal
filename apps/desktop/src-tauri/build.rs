fn main() {
    let dist = std::path::Path::new("../dist");
    if !dist.join("index.html").exists() {
        std::fs::create_dir_all(dist).expect("pasta dist");
        std::fs::write(
            dist.join("index.html"),
            "<!doctype html><html lang=\"pt-BR\"><title>Vortex Desktop</title></html>",
        )
        .expect("indice do dist");
    }
    let attributes =
        tauri_build::Attributes::new().app_manifest(tauri_build::AppManifest::new().commands(&[
            "get_terminal",
            "get_chart",
            "get_history",
            "get_config",
            "set_theme",
            "health_live",
            "health_ready",
            "get_status",
            "submit_order",
            "chart_preview",
            "revise_order",
            "cancel_order",
            "place_protection",
            "flatten",
            "reverse",
            "emergency_flat",
            "reset_session",
            "confirm_danger",
            "get_performance",
            "get_trades",
            "export_trades_csv",
        ]));
    tauri_build::try_build(attributes).expect("falha ao preparar o Tauri");
}
