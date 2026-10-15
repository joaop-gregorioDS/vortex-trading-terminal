use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

use tauri::{AppHandle, Runtime};
use tauri_plugin_dialog::{DialogExt, MessageDialogButtons};

use crate::dto::{DangerAction, DangerToken};
use crate::error::ApiError;

#[derive(Clone, Default)]
pub struct DangerGate {
    inner: Arc<Mutex<Option<Pending>>>,
}

struct Pending {
    action: DangerAction,
    token: String,
    expires: Instant,
}

impl DangerGate {
    pub fn new() -> Self {
        Self::default()
    }

    pub fn issue(&self, action: DangerAction) -> Result<String, ApiError> {
        self.store(action, Duration::from_secs(30))
    }

    pub fn take(&self, action: DangerAction, token: &str) -> Result<(), ApiError> {
        let mut guard = self
            .inner
            .lock()
            .map_err(|_| ApiError::danger("A confirmação não pôde ser lida."))?;
        let Some(pending) = guard.take() else {
            return Err(ApiError::danger("Esta confirmação expirou."));
        };
        if pending.token != token || pending.action != action || Instant::now() >= pending.expires {
            return Err(ApiError::danger(
                "Esta confirmação não vale para esta ação.",
            ));
        }
        Ok(())
    }

    fn store(&self, action: DangerAction, ttl: Duration) -> Result<String, ApiError> {
        let token = random_token()?;
        let mut guard = self
            .inner
            .lock()
            .map_err(|_| ApiError::danger("A confirmação não pôde ser gravada."))?;
        *guard = Some(Pending {
            action,
            token: token.clone(),
            expires: Instant::now() + ttl,
        });
        Ok(token)
    }
}

pub(crate) async fn confirm<R: Runtime>(
    gate: &DangerGate,
    action: DangerAction,
    app: AppHandle<R>,
) -> Result<DangerToken, ApiError> {
    let (title, message) = prompt(action);
    let accepted = tauri::async_runtime::spawn_blocking(move || {
        app.dialog()
            .message(message)
            .title(title)
            .buttons(MessageDialogButtons::YesNo)
            .blocking_show()
    })
    .await
    .map_err(|_| ApiError::contract("O diálogo não abriu."))?;
    if !accepted {
        return Err(ApiError::cancelled());
    }
    Ok(DangerToken {
        token: gate.issue(action)?,
    })
}

fn prompt(action: DangerAction) -> (&'static str, &'static str) {
    match action {
        DangerAction::Reset => (
            "Resetar a sessão?",
            "A conta de demonstração é compartilhada. O reset reabre a sessão para todos.",
        ),
        DangerAction::EmergencyFlat => (
            "Zerar tudo?",
            "Cancela as ordens abertas e zera as posições da conta de demonstração.",
        ),
        DangerAction::Reverse => (
            "Inverter a posição?",
            "A posição deste ativo será invertida na conta de demonstração.",
        ),
    }
}

fn random_token() -> Result<String, ApiError> {
    let mut bytes = [0u8; 32];
    getrandom::getrandom(&mut bytes)
        .map_err(|_| ApiError::contract("Não foi possível gerar a confirmação."))?;
    const HEX: &[u8; 16] = b"0123456789abcdef";
    let mut token = String::with_capacity(64);
    for byte in bytes {
        token.push(HEX[(byte >> 4) as usize] as char);
        token.push(HEX[(byte & 0x0f) as usize] as char);
    }
    Ok(token)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn token_is_single_use_and_bound_to_the_action() {
        let gate = DangerGate::new();
        let token = gate.issue(DangerAction::Reset).unwrap();
        assert!(gate.take(DangerAction::Reverse, &token).is_err());
        assert!(gate.take(DangerAction::Reset, &token).is_err());

        let token = gate.issue(DangerAction::EmergencyFlat).unwrap();
        assert!(gate.take(DangerAction::EmergencyFlat, &token).is_ok());
        assert!(gate.take(DangerAction::EmergencyFlat, &token).is_err());
    }

    #[test]
    fn expired_token_is_refused() {
        let gate = DangerGate::new();
        let token = gate.store(DangerAction::Reset, Duration::ZERO).unwrap();
        std::thread::sleep(Duration::from_millis(2));
        assert!(gate.take(DangerAction::Reset, &token).is_err());
    }
}
