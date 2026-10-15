use std::time::Duration;

use reqwest::Method;
use serde::de::DeserializeOwned;
use serde::{Deserialize, Serialize};
use url::Url;

use crate::dto::{
    parse_chart_body, ChartBody, ChartPreview, ChartPreviewRequest, ClosedTrade, DesktopConfig,
    EmergencyFlat, HealthText, Order, OrderRequest, PerformanceReport, PositionAction,
    ProtectionLevelRequest, ResetAck, StatusResponse, TerminalSnapshot, Theme, YahooHistory,
};
use crate::error::{ApiError, ApiErrorKind};

pub const VERSION: &str = env!("CARGO_PKG_VERSION");
pub const DEFAULT_BASE: &str = "https://vortexsoftware.tech";
pub const ENV_BASE: &str = "VORTEX_DESKTOP_API_BASE";

#[derive(Clone)]
pub struct ApiClient {
    http: reqwest::Client,
    base: Url,
}

pub fn parse_base_url(raw: &str) -> Result<Url, ApiError> {
    let trimmed = raw.trim().trim_end_matches('/');
    let url = Url::parse(trimmed).map_err(|_| ApiError::contract("URL base inválida."))?;
    if !url.username().is_empty() || url.password().is_some() {
        return Err(ApiError::contract(
            "A URL base não pode levar usuário ou senha.",
        ));
    }
    if url.path() != "" && url.path() != "/" {
        return Err(ApiError::contract("A URL base não pode levar caminho."));
    }
    if url.query().is_some() || url.fragment().is_some() {
        return Err(ApiError::contract("A URL base não pode levar consulta."));
    }
    match url.scheme() {
        "https" => {}
        "http" => {
            let host = url.host_str().unwrap_or("");
            let loopback = host == "127.0.0.1" || host == "localhost";
            if !loopback || url.port() != Some(5187) {
                return Err(ApiError::contract(
                    "HTTP só é aceito em 127.0.0.1:5187 ou localhost:5187.",
                ));
            }
        }
        _ => return Err(ApiError::contract("A URL base precisa ser http ou https.")),
    }
    if url.host_str().is_none() {
        return Err(ApiError::contract("A URL base não tem host."));
    }
    Ok(url)
}

pub fn display_config(url: &Url) -> DesktopConfig {
    let host = url.host_str().unwrap_or("");
    DesktopConfig {
        scheme: url.scheme().to_owned(),
        host: match url.port() {
            Some(port) => format!("{host}:{port}"),
            None => host.to_owned(),
        },
        theme: Theme::System,
    }
}

impl ApiClient {
    pub fn from_env() -> Result<Self, ApiError> {
        let raw = std::env::var(ENV_BASE).unwrap_or_else(|_| DEFAULT_BASE.to_owned());
        Self::from_parsed(parse_base_url(&raw)?)
    }

    /// O teste usa um host do wiremock. Os commands de produção passam por `from_env`.
    pub(crate) fn from_parsed(base: Url) -> Result<Self, ApiError> {
        let mut headers = reqwest::header::HeaderMap::new();
        headers.insert(
            reqwest::header::HeaderName::from_static("x-vortex-client"),
            reqwest::header::HeaderValue::from_str(&format!("vortex-desktop/{VERSION}"))
                .map_err(|_| ApiError::contract("Versão do cliente inválida."))?,
        );
        let http = reqwest::Client::builder()
            .user_agent(format!("vortex-desktop/{VERSION}"))
            .default_headers(headers)
            .redirect(reqwest::redirect::Policy::limited(3))
            .build()
            .map_err(|_| ApiError::transport())?;
        Ok(Self { http, base })
    }

    pub fn config(&self) -> DesktopConfig {
        display_config(&self.base)
    }

    pub async fn terminal(&self) -> Result<TerminalSnapshot, ApiError> {
        let url = self.join("api/terminal")?;
        self.get_json(url, Duration::from_secs(5)).await
    }

    /// Uma tentativa. O poller tem o próprio backoff e não repete dentro da chamada.
    pub async fn terminal_once(&self) -> Result<TerminalSnapshot, ApiError> {
        let url = self.join("api/terminal")?;
        let response = self
            .once(Method::GET, url, Duration::from_secs(5), None)
            .await?;
        let response = problem_or_ok(response).await?;
        let bytes = response
            .bytes()
            .await
            .map(|bytes| bytes.to_vec())
            .map_err(map_reqwest)?;
        serde_json::from_slice(&bytes).map_err(|error| {
            tracing::debug!(%error, "resposta fora do contrato");
            ApiError::contract("A resposta da API não cabe no contrato do cliente.")
        })
    }

    /// 200 e 503 são leitura. 503 não vira erro: o painel mostra o texto e o status.
    pub async fn health(&self, path: &str) -> Result<HealthText, ApiError> {
        let url = self.join(path)?;
        let response = self
            .exchange(Method::GET, url, Duration::from_secs(5), None)
            .await?;
        let status = response.status().as_u16();
        if status == 200 || status == 503 {
            let body = response.text().await.map_err(map_reqwest)?;
            let body = body.trim().to_owned();
            if body.is_empty() {
                return Err(ApiError::contract(
                    "A resposta de saúde não cabe no contrato do cliente.",
                ));
            }
            return Ok(HealthText { status, body });
        }
        problem_or_ok(response).await?;
        Err(ApiError::contract(
            "A resposta de saúde não cabe no contrato do cliente.",
        ))
    }

    pub async fn performance(&self) -> Result<PerformanceReport, ApiError> {
        let url = self.join("api/reports/performance")?;
        self.get_json(url, Duration::from_secs(5)).await
    }

    /// `ticker` ausente lista todos os livros. O prazo é o GET de 5 s.
    pub async fn trades(&self, ticker: Option<&str>) -> Result<Vec<ClosedTrade>, ApiError> {
        let mut url = self.join("api/reports/trades")?;
        if let Some(ticker) = ticker {
            url.query_pairs_mut().append_pair("ticker", ticker);
        }
        self.get_json(url, Duration::from_secs(5)).await
    }

    pub async fn status(&self) -> Result<StatusResponse, ApiError> {
        let url = self.join("api/status")?;
        self.get_json(url, Duration::from_secs(5)).await
    }

    pub async fn chart(
        &self,
        ticker: &str,
        interval: &str,
        max_bars: u32,
    ) -> Result<ChartBody, ApiError> {
        let mut url = self.join("api/chart")?;
        url.query_pairs_mut()
            .append_pair("ticker", ticker)
            .append_pair("interval", interval)
            .append_pair("maxBars", &max_bars.to_string());
        let bytes = self.get_bytes(url, Duration::from_secs(5)).await?;
        parse_chart_body(&bytes)
    }

    /// Prazo de 12 s. O GET repete só quando não houve corpo HTTP.
    pub async fn history(
        &self,
        ticker: &str,
        interval: &str,
        max_bars: u32,
    ) -> Result<YahooHistory, ApiError> {
        let mut url = self.join("api/market/history")?;
        url.query_pairs_mut()
            .append_pair("ticker", ticker)
            .append_pair("interval", interval)
            .append_pair("maxBars", &max_bars.to_string());
        self.get_json(url, Duration::from_secs(12)).await
    }

    pub async fn submit_order(&self, request: &OrderRequest) -> Result<Order, ApiError> {
        self.post_json("api/orders", request, Duration::from_secs(5))
            .await
    }

    pub async fn chart_preview(
        &self,
        request: &ChartPreviewRequest,
    ) -> Result<ChartPreview, ApiError> {
        self.post_json("api/orders/chart-preview", request, Duration::from_secs(12))
            .await
    }

    pub async fn revise_order(&self, order_id: &str, price: &str) -> Result<Order, ApiError> {
        self.post_json(
            &format!("api/orders/{order_id}/revise"),
            &serde_json::json!({ "price": price }),
            Duration::from_secs(5),
        )
        .await
    }

    pub async fn cancel_order(&self, order_id: &str) -> Result<Order, ApiError> {
        self.post_json(
            &format!("api/orders/{order_id}/cancel"),
            &serde_json::json!({}),
            Duration::from_secs(5),
        )
        .await
    }

    pub async fn place_protection(
        &self,
        request: &ProtectionLevelRequest,
    ) -> Result<Order, ApiError> {
        self.post_json("api/orders/protection", request, Duration::from_secs(12))
            .await
    }

    pub async fn flatten(&self, instrument_id: &str) -> Result<PositionAction, ApiError> {
        self.post_json(
            "api/orders/flatten",
            &serde_json::json!({ "instrumentId": instrument_id }),
            Duration::from_secs(12),
        )
        .await
    }

    pub async fn reverse(&self, instrument_id: &str) -> Result<PositionAction, ApiError> {
        self.post_json(
            "api/orders/reverse",
            &serde_json::json!({ "instrumentId": instrument_id }),
            Duration::from_secs(12),
        )
        .await
    }

    pub async fn emergency_flat(&self) -> Result<EmergencyFlat, ApiError> {
        self.post_json(
            "api/orders/emergency-flat",
            &serde_json::json!({}),
            Duration::from_secs(12),
        )
        .await
    }

    pub async fn reset_session(&self) -> Result<ResetAck, ApiError> {
        self.post_json(
            "api/session/reset",
            &serde_json::json!({}),
            Duration::from_secs(12),
        )
        .await
    }

    fn join(&self, path: &str) -> Result<Url, ApiError> {
        self.base
            .join(path)
            .map_err(|_| ApiError::contract("Caminho da API inválido."))
    }

    async fn get_json<T: DeserializeOwned>(
        &self,
        url: Url,
        timeout: Duration,
    ) -> Result<T, ApiError> {
        let bytes = self.get_bytes(url, timeout).await?;
        serde_json::from_slice(&bytes).map_err(|error| {
            tracing::debug!(%error, "resposta fora do contrato");
            ApiError::contract("A resposta da API não cabe no contrato do cliente.")
        })
    }

    async fn get_bytes(&self, url: Url, timeout: Duration) -> Result<Vec<u8>, ApiError> {
        let response = self.send(Method::GET, url, timeout).await?;
        response
            .bytes()
            .await
            .map(|bytes| bytes.to_vec())
            .map_err(map_reqwest)
    }

    /// POST não repete. GET repete só quando não houve resposta HTTP.
    pub(crate) async fn send(
        &self,
        method: Method,
        url: Url,
        timeout: Duration,
    ) -> Result<reqwest::Response, ApiError> {
        let response = self.exchange(method, url, timeout, None).await?;
        problem_or_ok(response).await
    }

    /// POST com corpo. Uma tentativa, também quando a leitura do corpo falha depois da resposta.
    pub async fn post_json<T, B>(
        &self,
        path: &str,
        body: &B,
        timeout: Duration,
    ) -> Result<T, ApiError>
    where
        T: DeserializeOwned,
        B: Serialize,
    {
        let url = self.join(path)?;
        let bytes = serde_json::to_vec(body)
            .map_err(|_| ApiError::contract("A ordem não cabe no contrato do cliente."))?;
        let response = self
            .exchange(Method::POST, url, timeout, Some(bytes))
            .await?;
        let response = problem_or_ok(response).await?;
        let payload = response.bytes().await.map_err(map_reqwest)?;
        serde_json::from_slice(&payload).map_err(|error| {
            tracing::debug!(%error, "resposta fora do contrato");
            ApiError::contract("A resposta da API não cabe no contrato do cliente.")
        })
    }

    async fn exchange(
        &self,
        method: Method,
        url: Url,
        timeout: Duration,
        body: Option<Vec<u8>>,
    ) -> Result<reqwest::Response, ApiError> {
        let attempts = if method == Method::GET { 3 } else { 1 };
        let mut last = ApiError::transport();
        for attempt in 0..attempts {
            match self
                .once(method.clone(), url.clone(), timeout, body.clone())
                .await
            {
                Ok(response) => return Ok(response),
                Err(error) if is_retryable(&error) && attempt + 1 < attempts => {
                    last = error;
                    tokio::time::sleep(retry_delay(attempt)).await;
                }
                Err(error) => return Err(error),
            }
        }
        Err(last)
    }

    async fn once(
        &self,
        method: Method,
        url: Url,
        timeout: Duration,
        body: Option<Vec<u8>>,
    ) -> Result<reqwest::Response, ApiError> {
        let path = url.path().to_owned();
        let mut request = self.http.request(method, url).timeout(timeout);
        if let Some(body) = body {
            request = request
                .header(reqwest::header::CONTENT_TYPE, "application/json")
                .body(body);
        }
        let result = request.send().await.map_err(map_reqwest);
        match &result {
            Ok(response) => tracing::debug!(%path, status = %response.status(), "api"),
            Err(error) => tracing::debug!(%path, kind = ?error.kind, "api"),
        }
        result
    }
}

fn is_retryable(error: &ApiError) -> bool {
    matches!(error.kind, ApiErrorKind::Transport | ApiErrorKind::Timeout)
}

fn retry_delay(attempt: u32) -> Duration {
    let base = if attempt == 0 { 200 } else { 500 };
    jittered(base, timer_entropy())
}

fn timer_entropy() -> u64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|duration| duration.subsec_nanos() as u64)
        .unwrap_or(0)
}

fn jittered(base_ms: u64, entropy: u64) -> Duration {
    let spread = base_ms / 5;
    let delta = if spread == 0 {
        0
    } else {
        entropy % (spread * 2)
    };
    Duration::from_millis(base_ms - spread + delta)
}

fn map_reqwest(error: reqwest::Error) -> ApiError {
    if error.is_timeout() {
        ApiError::timeout()
    } else {
        ApiError::transport()
    }
}

async fn problem_or_ok(response: reqwest::Response) -> Result<reqwest::Response, ApiError> {
    let status = response.status();
    if status.is_success() {
        return Ok(response);
    }
    let code_status = status.as_u16();
    let body = response.text().await.unwrap_or_default();
    let problem = serde_json::from_str::<ProblemBody>(&body).ok();
    let title = problem
        .as_ref()
        .and_then(|item| item.title.clone())
        .filter(|title| !title.is_empty())
        .unwrap_or_else(|| "A API recusou a operação.".into());
    Err(ApiError::problem(
        problem
            .as_ref()
            .and_then(|item| item.status)
            .unwrap_or(code_status),
        problem.as_ref().and_then(|item| item.code.clone()),
        title,
        problem.and_then(|item| item.trace_id),
    ))
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct ProblemBody {
    title: Option<String>,
    status: Option<u16>,
    code: Option<String>,
    trace_id: Option<String>,
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::dto::{ChartBody, DecimalString, Order};
    use crate::error::ApiErrorKind;

    #[test]
    fn base_url_policy() {
        let production = parse_base_url(" https://vortexsoftware.tech/ ").unwrap();
        assert_eq!(production.scheme(), "https");
        assert_eq!(production.host_str(), Some("vortexsoftware.tech"));
        assert_eq!(production.path(), "/");
        let shown = display_config(&production);
        assert_eq!(shown.scheme, "https");
        assert_eq!(shown.host, "vortexsoftware.tech");
        assert_eq!(shown.theme, Theme::System);

        let local = parse_base_url("http://127.0.0.1:5187").unwrap();
        assert_eq!(display_config(&local).host, "127.0.0.1:5187");
        assert!(parse_base_url("http://localhost:5187").is_ok());
        assert!(parse_base_url("https://vortexsoftware.tech/api").is_err());
        assert!(parse_base_url("https://user:secret@vortexsoftware.tech").is_err());
        assert!(parse_base_url("https://vortexsoftware.tech?x=1").is_err());
        assert!(parse_base_url("http://127.0.0.1:8080").is_err());
        assert!(parse_base_url("http://example.com").is_err());
        assert!(parse_base_url("ftp://vortexsoftware.tech").is_err());

        let joined = production.join("api/terminal").unwrap();
        assert_eq!(joined.as_str(), "https://vortexsoftware.tech/api/terminal");
    }

    #[test]
    fn decimal_roundtrip_keeps_27_places() {
        let raw = "1.000000000000000000000000001";
        let value = DecimalString::parse(raw).unwrap();
        let json = serde_json::to_string(&value).unwrap();
        assert_eq!(json, format!("\"{raw}\""));
        assert!(!json.contains('e') && !json.contains('E'));
        let back: DecimalString = serde_json::from_str(&json).unwrap();
        assert_eq!(back.as_str(), raw);
    }

    #[test]
    fn decimal_rejects_scientific_notation_and_scale_above_28() {
        assert!(DecimalString::parse("1e-2").is_err());
        assert!(DecimalString::parse("1.").is_err());
        assert!(DecimalString::parse(&format!("1.{}", "1".repeat(29))).is_err());
        let order = sample_order("rejected", Some("sem saldo"));
        let error = order.rejected_error().unwrap();
        assert_eq!(error.kind, ApiErrorKind::Rejected);
        assert_eq!(error.status, Some(201));
        assert_eq!(error.code.as_deref(), Some("sem saldo"));
        assert_eq!(error.title, "sem saldo");
        assert!(sample_order("accepted", None).rejected_error().is_none());
    }

    #[test]
    fn chart_empty_does_not_swallow_a_snapshot() {
        let empty = parse_chart_body(br#"{"status":"empty"}"#).unwrap();
        assert!(matches!(empty, ChartBody::Empty { .. }));
        assert!(parse_chart_body(br#"{"status":"empty","candles":[]}"#).is_err());
        let snapshot = parse_chart_body(include_bytes!("../tests/fixtures/chart.json")).unwrap();
        match snapshot {
            ChartBody::Snapshot(body) => {
                assert_eq!(body.candles.len(), 1);
                assert_eq!(body.candles[0].close.as_str(), "36.50");
                assert!(body.indicators[0].parameters.get("multiplier").is_some());
            }
            ChartBody::Empty { .. } => panic!("snapshot lido como vazio"),
        }
    }

    #[test]
    fn terminal_fixture_keeps_unknown_fields_and_exact_decimal() {
        let snapshot: TerminalSnapshot =
            serde_json::from_str(include_str!("../tests/fixtures/terminal.json")).unwrap();
        assert_eq!(snapshot.quotes[0].ticker, "PETR4");
        assert_eq!(
            snapshot.portfolio.total_profit_and_loss.as_str(),
            "1.000000000000000000000000001"
        );
        assert_eq!(
            snapshot.quotes[0].instrument_id,
            "11111111-1111-1111-1111-111111111111"
        );
    }

    #[tokio::test]
    async fn problem_details_and_single_success_attempt() {
        let server = wiremock::MockServer::start().await;
        wiremock::Mock::given(wiremock::matchers::method("GET"))
            .and(wiremock::matchers::path("/api/terminal"))
            .and(wiremock::matchers::header(
                "x-vortex-client",
                format!("vortex-desktop/{VERSION}").as_str(),
            ))
            .respond_with(wiremock::ResponseTemplate::new(503).set_body_string(
                r#"{"title":"A cotação do Yahoo não respondeu.","status":503,"code":"yahoo_unavailable","traceId":"abc","detail":null}"#,
            ))
            .expect(1)
            .mount(&server)
            .await;
        let client = ApiClient::from_parsed(Url::parse(&server.uri()).unwrap()).unwrap();
        let error = client.terminal().await.unwrap_err();
        assert_eq!(error.kind, ApiErrorKind::Problem);
        assert_eq!(error.status, Some(503));
        assert_eq!(error.code.as_deref(), Some("yahoo_unavailable"));
        assert_eq!(error.trace_id.as_deref(), Some("abc"));
        assert_eq!(error.title, "A cotação do Yahoo não respondeu.");
    }

    #[tokio::test]
    async fn terminal_over_http_reads_the_fixture() {
        let server = wiremock::MockServer::start().await;
        wiremock::Mock::given(wiremock::matchers::method("GET"))
            .and(wiremock::matchers::path("/api/terminal"))
            .respond_with(
                wiremock::ResponseTemplate::new(200)
                    .set_body_string(include_str!("../tests/fixtures/terminal.json")),
            )
            .expect(1)
            .mount(&server)
            .await;
        let client = ApiClient::from_parsed(Url::parse(&server.uri()).unwrap()).unwrap();
        let snapshot = client.terminal_once().await.unwrap();
        assert_eq!(snapshot.session_open, Some(true));
        assert_eq!(snapshot.portfolio.cash.as_str(), "100000.00");
    }

    #[tokio::test]
    async fn get_retries_without_an_http_body_and_post_does_not() {
        let get_count = count_dropped_attempts(Method::GET).await;
        let post_count = count_dropped_attempts(Method::POST).await;
        assert_eq!(get_count, 3);
        assert_eq!(post_count, 1);
    }

    async fn count_dropped_attempts(method: Method) -> u32 {
        let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
        let port = listener.local_addr().unwrap().port();
        let accepts = std::sync::Arc::new(std::sync::atomic::AtomicU32::new(0));
        let count = accepts.clone();
        tokio::spawn(async move {
            loop {
                match listener.accept().await {
                    Ok((socket, _)) => {
                        count.fetch_add(1, std::sync::atomic::Ordering::SeqCst);
                        drop(socket);
                    }
                    Err(_) => break,
                }
            }
        });
        let client =
            ApiClient::from_parsed(Url::parse(&format!("http://127.0.0.1:{port}/")).unwrap())
                .unwrap();
        let url = Url::parse(&format!("http://127.0.0.1:{port}/api/terminal")).unwrap();
        let error = client
            .send(method, url, Duration::from_millis(400))
            .await
            .unwrap_err();
        assert!(matches!(
            error.kind,
            ApiErrorKind::Transport | ApiErrorKind::Timeout
        ));
        accepts.load(std::sync::atomic::Ordering::SeqCst)
    }

    #[tokio::test]
    #[ignore]
    async fn live_terminal_on_local_api() {
        let client = ApiClient::from_parsed(Url::parse("http://127.0.0.1:5187").unwrap()).unwrap();
        let snapshot = client
            .terminal()
            .await
            .expect("API local em 127.0.0.1:5187");
        assert!(!snapshot.quotes.is_empty());
    }

    #[tokio::test]
    async fn health_keeps_503_as_text_and_status_keeps_the_decimal() {
        let server = wiremock::MockServer::start().await;
        wiremock::Mock::given(wiremock::matchers::method("GET"))
            .and(wiremock::matchers::path("/health/live"))
            .respond_with(wiremock::ResponseTemplate::new(503).set_body_string("Unhealthy\n"))
            .expect(1)
            .mount(&server)
            .await;
        wiremock::Mock::given(wiremock::matchers::method("GET"))
            .and(wiremock::matchers::path("/health/ready"))
            .respond_with(wiremock::ResponseTemplate::new(200).set_body_string("Healthy"))
            .expect(1)
            .mount(&server)
            .await;
        wiremock::Mock::given(wiremock::matchers::method("GET"))
            .and(wiremock::matchers::path("/api/status"))
            .respond_with(
                wiremock::ResponseTemplate::new(200)
                    .set_body_string(include_str!("../tests/fixtures/status.json")),
            )
            .expect(1)
            .mount(&server)
            .await;
        let client = ApiClient::from_parsed(Url::parse(&server.uri()).unwrap()).unwrap();
        let live = client.health("health/live").await.unwrap();
        assert_eq!(live.status, 503);
        assert_eq!(live.body, "Unhealthy");
        let ready = client.health("health/ready").await.unwrap();
        assert_eq!(ready.status, 200);
        assert_eq!(ready.body, "Healthy");
        let status = client.status().await.unwrap();
        assert_eq!(
            status.traded_quantity.as_str(),
            "1.000000000000000000000000001"
        );
        assert_eq!(status.last_price.unwrap().as_str(), "36.50");
        assert_eq!(status.order_count, 2);
    }

    #[tokio::test]
    async fn empty_health_body_is_a_contract_error() {
        let server = wiremock::MockServer::start().await;
        wiremock::Mock::given(wiremock::matchers::method("GET"))
            .and(wiremock::matchers::path("/health/live"))
            .respond_with(wiremock::ResponseTemplate::new(200).set_body_string("  "))
            .expect(1)
            .mount(&server)
            .await;
        let client = ApiClient::from_parsed(Url::parse(&server.uri()).unwrap()).unwrap();
        let error = client.health("health/live").await.unwrap_err();
        assert_eq!(error.kind, ApiErrorKind::Contract);
    }

    #[tokio::test]
    async fn history_keeps_the_server_decimals_and_ignores_unknown_fields() {
        let server = wiremock::MockServer::start().await;
        wiremock::Mock::given(wiremock::matchers::method("GET"))
            .and(wiremock::matchers::path("/api/market/history"))
            .and(wiremock::matchers::query_param("ticker", "PETR4"))
            .and(wiremock::matchers::query_param("interval", "1d"))
            .and(wiremock::matchers::query_param("maxBars", "180"))
            .respond_with(
                wiremock::ResponseTemplate::new(200)
                    .set_body_string(include_str!("../tests/fixtures/history.json")),
            )
            .expect(1)
            .mount(&server)
            .await;
        let client = ApiClient::from_parsed(Url::parse(&server.uri()).unwrap()).unwrap();
        let history = client.history("PETR4", "1d", 180).await.unwrap();
        assert_eq!(history.yahoo_symbol, "PETR4.SA");
        assert_eq!(history.candles[0].close.as_str(), "36.50");
        assert_eq!(history.candles[0].volume.as_str(), "1000");
        assert_eq!(history.tick_size.as_str(), "0.01");
        assert!(history.forming_minutes.is_empty());
        assert!(history.delayed);
    }

    #[tokio::test]
    async fn rejected_creation_is_read_once() {
        let server = wiremock::MockServer::start().await;
        wiremock::Mock::given(wiremock::matchers::method("POST"))
            .and(wiremock::matchers::path("/api/orders"))
            .respond_with(
                wiremock::ResponseTemplate::new(201).set_body_string(rejected_body("sem saldo")),
            )
            .expect(1)
            .mount(&server)
            .await;
        let client = ApiClient::from_parsed(Url::parse(&server.uri()).unwrap()).unwrap();
        let order = client.submit_order(&market_request()).await.unwrap();
        let error = order.rejected_error().unwrap();
        assert_eq!(error.kind, ApiErrorKind::Rejected);
        assert_eq!(error.title, "sem saldo");
        assert_eq!(error.status, Some(201));
    }

    #[tokio::test]
    async fn post_does_not_retry_after_the_server_answers() {
        let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
        let port = listener.local_addr().unwrap().port();
        let hits = std::sync::Arc::new(std::sync::atomic::AtomicU32::new(0));
        let count = hits.clone();
        tokio::spawn(async move {
            loop {
                let Ok((mut socket, _)) = listener.accept().await else {
                    break;
                };
                count.fetch_add(1, std::sync::atomic::Ordering::SeqCst);
                let mut buffer = [0u8; 4096];
                let _ = tokio::time::timeout(
                    Duration::from_secs(2),
                    tokio::io::AsyncReadExt::read(&mut socket, &mut buffer),
                )
                .await;
                let _ = tokio::io::AsyncWriteExt::write_all(
                    &mut socket,
                    b"HTTP/1.1 201 Created\r\nContent-Length: 20\r\nConnection: close\r\n\r\n",
                )
                .await;
                drop(socket);
            }
        });
        let client =
            ApiClient::from_parsed(Url::parse(&format!("http://127.0.0.1:{port}/")).unwrap())
                .unwrap();
        let error = client.submit_order(&market_request()).await.unwrap_err();
        assert!(matches!(
            error.kind,
            ApiErrorKind::Transport | ApiErrorKind::Timeout | ApiErrorKind::Contract
        ));
        assert_eq!(hits.load(std::sync::atomic::Ordering::SeqCst), 1);
    }

    #[tokio::test]
    async fn performance_keeps_blank_ratios_and_trades_keep_the_decimal() {
        let server = wiremock::MockServer::start().await;
        wiremock::Mock::given(wiremock::matchers::method("GET"))
            .and(wiremock::matchers::path("/api/reports/performance"))
            .respond_with(wiremock::ResponseTemplate::new(200).set_body_string(
                r#"{
                    "startingBalance":"100000","currentBalance":"100000","equity":"100000",
                    "realizedPnl":"0","dailyPnl":"0","winRate":"0.5",
                    "winsCount":1,"lossesCount":1,"totalTrades":2,
                    "profitFactor":"","payoff":"","maxDrawdown":"0",
                    "bestTrade":null,"worstTrade":null,"equityCurve":[]
                }"#,
            ))
            .expect(1)
            .mount(&server)
            .await;
        wiremock::Mock::given(wiremock::matchers::method("GET"))
            .and(wiremock::matchers::path("/api/reports/trades"))
            .and(wiremock::matchers::query_param("ticker", "PETR4"))
            .respond_with(wiremock::ResponseTemplate::new(200).set_body_string(
                r#"[{
                    "id":"t1","timestamp":"2026-10-14T13:00:00.0000000Z","ticker":"PETR4",
                    "market":"daytrade","type":"buy","quantity":"100",
                    "entryPrice":"36.50","exitPrice":"36.80","pnl":"-1.25"
                }]"#,
            ))
            .expect(1)
            .mount(&server)
            .await;
        let client = ApiClient::from_parsed(Url::parse(&server.uri()).unwrap()).unwrap();
        let report = client.performance().await.unwrap();
        assert_eq!(report.win_rate.as_str(), "0.5");
        assert_eq!(report.profit_factor.as_str(), "");
        assert_eq!(report.payoff.as_str(), "");
        let trades = client.trades(Some("PETR4")).await.unwrap();
        assert_eq!(trades[0].entry_price.as_str(), "36.50");
        assert_eq!(trades[0].pnl.as_str(), "-1.25");
    }

    fn market_request() -> crate::dto::OrderRequest {
        crate::dto::OrderRequest {
            instrument_id: "11111111-1111-1111-1111-111111111111".into(),
            side: "buy".into(),
            r#type: Some("market".into()),
            quantity: "1".into(),
            limit_price: None,
            time_in_force: None,
            origin: None,
            protection: None,
        }
    }

    fn rejected_body(reason: &str) -> String {
        format!(
            r#"{{
                "orderId": "11111111-1111-1111-1111-111111111111",
                "instrumentId": "11111111-1111-1111-1111-111111111111",
                "side": "buy",
                "type": "market",
                "origin": "session",
                "quantity": "1",
                "limitPrice": null,
                "timeInForce": "gtc",
                "status": "rejected",
                "filledQuantity": "0",
                "remainingQuantity": "1",
                "rejectionReason": "{reason}",
                "createdAt": "2026-10-14T13:00:00Z",
                "lastUpdatedAt": "2026-10-14T13:00:00Z",
                "completedAt": null,
                "fills": []
            }}"#
        )
    }

    fn sample_order(status: &str, reason: Option<&str>) -> Order {
        let reason = reason
            .map(|item| format!("\"{item}\""))
            .unwrap_or_else(|| "null".into());
        serde_json::from_str(&format!(
            r#"{{
                "orderId": "1",
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
                "rejectionReason": {reason},
                "createdAt": "2026-10-14T13:00:00Z",
                "lastUpdatedAt": "2026-10-14T13:00:00Z",
                "completedAt": null,
                "fills": []
            }}"#
        ))
        .unwrap()
    }
}
