use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, ts_rs::TS)]
#[serde(rename_all = "camelCase")]
#[ts(export)]
pub enum ApiErrorKind {
    Transport,
    Timeout,
    Problem,
    Rejected,
    Contract,
    Cancelled,
    Danger,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize, ts_rs::TS)]
#[serde(rename_all = "camelCase")]
#[ts(export)]
pub struct ApiError {
    pub kind: ApiErrorKind,
    pub code: Option<String>,
    pub title: String,
    pub status: Option<u16>,
    pub trace_id: Option<String>,
}

impl ApiError {
    pub fn transport() -> Self {
        Self {
            kind: ApiErrorKind::Transport,
            code: None,
            title: "Sem conexão com a API.".into(),
            status: None,
            trace_id: None,
        }
    }

    pub fn timeout() -> Self {
        Self {
            kind: ApiErrorKind::Timeout,
            code: None,
            title: "A API não respondeu a tempo.".into(),
            status: None,
            trace_id: None,
        }
    }

    pub fn contract(title: impl Into<String>) -> Self {
        Self {
            kind: ApiErrorKind::Contract,
            code: None,
            title: title.into(),
            status: None,
            trace_id: None,
        }
    }

    pub fn problem(
        status: u16,
        code: Option<String>,
        title: impl Into<String>,
        trace_id: Option<String>,
    ) -> Self {
        Self {
            kind: ApiErrorKind::Problem,
            code,
            title: title.into(),
            status: Some(status),
            trace_id,
        }
    }

    pub fn cancelled() -> Self {
        Self {
            kind: ApiErrorKind::Cancelled,
            code: None,
            title: "Ação cancelada.".into(),
            status: None,
            trace_id: None,
        }
    }

    pub fn danger(title: impl Into<String>) -> Self {
        Self {
            kind: ApiErrorKind::Danger,
            code: Some("danger".into()),
            title: title.into(),
            status: None,
            trace_id: None,
        }
    }
}

impl std::fmt::Display for ApiError {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(formatter, "{}", self.title)
    }
}

impl std::error::Error for ApiError {}
