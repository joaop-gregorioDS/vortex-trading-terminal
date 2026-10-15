use std::fs;
use std::path::Path;

use ts_rs::TS;

use crate::dto::{
    ChartBody, ChartPreview, ChartPreviewRequest, ConnectionState, CsvExport, DangerAction,
    DangerToken, DesktopConfig, DiagnosticReport, EmergencyFlat, FillNotice, HealthText,
    HistoryInterval, OrderRequest, PositionAction, ProtectionLevelRequest, ProtectionRequest,
    ResetAck, StatusResponse, TerminalSnapshot, YahooHistory,
};
use crate::error::ApiError;

#[test]
fn bindings_match_committed_sources() {
    let temp = std::env::temp_dir().join(format!("vortex-desktop-bindings-{}", std::process::id()));
    let _ = fs::remove_dir_all(&temp);
    fs::create_dir_all(&temp).unwrap();
    let config = ts_rs::Config::new().with_out_dir(&temp);
    TerminalSnapshot::export_all(&config).unwrap();
    ChartBody::export_all(&config).unwrap();
    ApiError::export_all(&config).unwrap();
    DesktopConfig::export_all(&config).unwrap();
    ConnectionState::export_all(&config).unwrap();
    DiagnosticReport::export_all(&config).unwrap();
    HealthText::export_all(&config).unwrap();
    StatusResponse::export_all(&config).unwrap();
    YahooHistory::export_all(&config).unwrap();
    HistoryInterval::export_all(&config).unwrap();
    OrderRequest::export_all(&config).unwrap();
    ProtectionRequest::export_all(&config).unwrap();
    ChartPreviewRequest::export_all(&config).unwrap();
    ChartPreview::export_all(&config).unwrap();
    ProtectionLevelRequest::export_all(&config).unwrap();
    PositionAction::export_all(&config).unwrap();
    EmergencyFlat::export_all(&config).unwrap();
    ResetAck::export_all(&config).unwrap();
    DangerAction::export_all(&config).unwrap();
    DangerToken::export_all(&config).unwrap();
    FillNotice::export_all(&config).unwrap();
    CsvExport::export_all(&config).unwrap();

    let committed = Path::new(env!("CARGO_MANIFEST_DIR")).join("../src/bindings");
    if std::env::var("VORTEX_WRITE_BINDINGS").ok().as_deref() == Some("1") {
        let _ = fs::remove_dir_all(&committed);
        fs::create_dir_all(&committed).unwrap();
        copy_dir(&temp, &committed);
        return;
    }

    let generated = file_map(&temp);
    let stored = file_map(&committed);
    assert_eq!(
        stored.keys().cloned().collect::<Vec<_>>(),
        generated.keys().cloned().collect::<Vec<_>>(),
        "bindings divergentes; gere de novo com VORTEX_WRITE_BINDINGS=1"
    );
    for (name, content) in generated {
        assert_eq!(
            stored.get(&name).map(String::as_str),
            Some(content.as_str()),
            "{name}"
        );
    }
}

fn file_map(dir: &Path) -> std::collections::BTreeMap<String, String> {
    let mut files = std::collections::BTreeMap::new();
    let entries = fs::read_dir(dir)
        .unwrap_or_else(|_| panic!("pasta de bindings ausente: {}", dir.display()));
    for entry in entries.flatten() {
        let path = entry.path();
        if path.extension().and_then(|ext| ext.to_str()) != Some("ts") {
            continue;
        }
        let name = entry.file_name().to_string_lossy().into_owned();
        let content = fs::read_to_string(&path).unwrap().replace("\r\n", "\n");
        files.insert(name, content);
    }
    files
}

fn copy_dir(from: &Path, to: &Path) {
    for (name, content) in file_map(from) {
        fs::write(to.join(name), content).unwrap();
    }
}
