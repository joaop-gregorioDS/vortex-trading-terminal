# ADR-047 — Apps desktop Linux e macOS em Tauri 2

- Status: Accepted
- Date: 2026-10-14
- Scope: Cliente desktop. Não altera a API, o pregão nem o comportamento do app web.

## Context

O Vortex é um terminal educacional de paper trading. A API .NET 10 em `https://vortexsoftware.tech` é a fonte da verdade: sessão única (ADR-039), decimais exatos em string (ADR-002), polling (ADR-041) e pregão Yahoo que congela fora de 10:00–17:00 America/Sao_Paulo (ADR-046). O web em `apps/web` consome essa API na mesma origem (ADR-041).

O produto precisa de apps Linux e macOS instaláveis, com a mesma estação (Visão Geral, Mesa, Histórico, Relatórios) e com integração nativa (menu, atalhos, bandeja, notificações, tema, janela). Apps instalados vivem mais que o web e não passam pelo rewrite do Next.

Versões confirmadas em 2026-10-14, não de memória: Tauri `2.12.3` (crate, MSRV Rust `1.90.0`), `@tauri-apps/api` `2.12.3`, `@tauri-apps/cli` `2.12.2`, `tauri-build` `2.7.2`. A documentação de distribuição diz que o mínimo padrão do bundle macOS é 10.13 e que o AppImage deve ser construído na base mais antiga que já tenha WebKitGTK 4.1. Ubuntu 22.04 e Debian 12 cumprem isso.

## Decision

Construir o **Vortex Desktop** em `apps/desktop/` como cliente fino Tauri 2, um código-fonte, dois alvos:

- macOS: `.app` e `.dmg` universal (`aarch64-apple-darwin` + `x86_64-apple-darwin`), assinados com Developer ID, hardened runtime, notarizados e com staple. Mínimo de produto: macOS 12.
- Linux: AppImage e `.deb`, buildados em Ubuntu 22.04. `.rpm` e Flatpak ficam fora deste corte.

Regras fixas:

1. Todo HTTP sai do núcleo Rust com `reqwest` `0.13.5` e `rustls`. A WebView não chama `fetch` para a API.
2. O front do desktop é Vite + React + TypeScript estático. Não é Next.js.
3. Os DTOs nascem no Rust. TypeScript é gerado por `ts-rs` `12.0.1`. Não há tipos de contrato escritos à mão.
4. Campos financeiros são `rust_decimal::Decimal` com a feature `serde-with-str`. É proibido `f32`/`f64` e é proibido `serde_json` com `arbitrary_precision`.
5. O estado de mercado é o snapshot de `GET /api/terminal`, entregue pelo evento `terminal://snapshot`. O store do front é Zustand `5.0.15`.
6. Cada operação de tela é um command Tauri fino que devolve `Result<T, ApiError>`. POST de ordem não tem retry. `POST /api/orders` com HTTP 201 e `status == "rejected"` vira `ApiError`.
7. O web continua na mesma origem. A extração para `packages/trading-core` e `packages/trading-ui` é fatia a fatia, e os arquivos `server-only` não saem de `apps/web`.
8. Reset, emergency-flat e reverse exigem um token de uso único emitido por um diálogo nativo aberto no Rust.
9. Não há segredo no binário. A URL base padrão é `https://vortexsoftware.tech`. Credencial futura fica no Keychain ou no Secret Service via `keyring` `4.2.0`.

O detalhe de componentes, IPC, ameaças, pacotes e fases está em `docs/desktop/`.

## Consequences

- O desktop não precisa de CORS e não expõe a URL base ao JavaScript.
- O cliente não calcula P&L, patrimônio, risco, classificação limit/stop nem preço de proteção.
- `instrumentId` é resolvido em tempo de execução pelo snapshot ou por `GET /api/market/quotes`.
- A conta continua compartilhada e sem autenticação até um ADR de backend. O app só reduz o dano com confirmação nativa.
- `tauri-specta` não entra. A linha v2 publicada segue em release candidate (`2.0.0-rc.*`, último push em 2026-07-26) e a issue de estabilidade para produção estava aberta em 2026-07-22, sem resposta. `ts-rs` `12.0.1` é estável e cobre os DTOs. As assinaturas dos commands ficam num módulo Rust único, com argumentos e retornos gerados.
- O README, o ADR-045 e o ADR-046 ainda dizem que stop e OCO não existem. O código já expõe `POST /api/orders/protection`, `flatten`, `reverse`, `revise`, `chart-preview` e o tipo `stop`. O desktop consome o código. Corrigir esses textos continua fora deste ADR.
- O estágio do README continua o ADR-046. Este ADR não entra nesse marcador: o guard do CI só exige `Accepted` para ADRs citados na seção de estágio, e o desktop ainda não substitui o corte do web.

## Alternatives

**Cliente Next dentro do Tauri, com `output: export`.** Rejeitado: o web depende de rewrite, Server Components e módulos `server-only` (`fetch-snapshot.ts`, `fetch-live-snapshot.ts`). Export estático reabre CORS ou embute a URL no JavaScript, o que o ADR-041 evitou no browser e o que este cliente não pode fazer.

**HTTP pelo `tauri-plugin-http` na WebView.** Rejeitado: devolve a URL e o retry ao JavaScript e exige capability de rede no documento. O plugin existe (`2.8.2`) e não será dependência.

**`tauri-specta` para gerar commands e events.** Adiado, não rejeitado para sempre: quando publicar `2.0.0` estável e acompanhar o Tauri `2.12`, um ADR curto pode trocar o gerador. Até lá o gerador é `ts-rs`.

**String opaca no lugar de `rust_decimal`.** Rejeitada para o núcleo: o CSV e a validação de entrada precisam rejeitar decimal inválido e ordenar sem float. A UI continua recebendo `string`, como o web.

**Redux Toolkit ou Jotai.** Rejeitados: o estado autoritativo é um documento só, o snapshot. Zustand guarda esse documento e a conexão sem normalizar o que o servidor já entregou.

**Atalho global para zerar ou resetar.** Rejeitado: `tauri-plugin-global-shortcut` `2.4.1` dispararia com outro app em foco, na conta compartilhada. Atalhos ficam no menu da janela.

## References

- ADR-001, ADR-002, ADR-039, ADR-041, ADR-044, ADR-045, ADR-046.
- `docs/desktop/architecture.md`
- `docs/desktop/ipc-contract.md`
- `docs/desktop/security.md`
- `docs/desktop/packaging-and-ci.md`
- `docs/desktop/backend-requests.md`
- `docs/desktop/phases.md`
- `docs/desktop/testing.md`
- Tauri 2.12.3: https://crates.io/crates/tauri e https://v2.tauri.app/release/
- AppImage e pré-requisitos: https://v2.tauri.app/distribute/appimage/ e https://v2.tauri.app/start/prerequisites/
- Bundle macOS: https://v2.tauri.app/distribute/macos-application-bundle/
