# ADR-048 — Licenças do binário Rust do Vortex Desktop

- Status: Accepted
- Date: 2026-10-14
- Scope: `apps/desktop/src-tauri`. Não altera a API, o web, o estágio do README nem o `ci.yml`.

## Context

`docs/desktop/packaging-and-ci.md` fecha a lista do `cargo deny` em MIT, Apache-2.0, BSD-3-Clause, ISC, Unicode-3.0 e MPL-2.0. MPL só entra se o grafo Rust realmente puxar um crate com essa licença, e nesse caso o ADR registra. GPL não entra sem outra decisão. Licença fora dessa lista só entra com o nome que o `cargo deny check` imprimir.

O check de 2026-10-14, com `cargo-deny` 0.20.2, `--locked`, alvos `x86_64-unknown-linux-gnu`, `aarch64-apple-darwin` e `x86_64-apple-darwin`, rejeitou o que segue.

MPL-2.0, no binário de todos os alvos, pela árvore do Tauri e não pelo binding do WebKit:

- `cssparser` 0.37.0, `cssparser-macros` 0.7.1, `dtoa-short` 0.3.5 e `selectors` 0.38.0, via `dom_query` e `tauri-utils`.
- `option-ext` 0.2.0, via `dirs-sys` e `dirs`.

Fora da lista original, o mesmo check pediu:

- `Zlib`, em `foldhash` 0.2.0, também via `dom_query`.
- `Apache-2.0 WITH LLVM-exception`, em `target-lexicon` 0.12.16, via `system-deps` no alvo Linux.

Não apareceu GPL. Não apareceu crate `openssl` nem `openssl-sys`. O HTTP continua em `rustls`.

## Decision

A lista em `apps/desktop/src-tauri/deny.toml` passa a permitir MPL-2.0, Zlib e Apache-2.0 WITH LLVM-exception, além das licenças já nomeadas no documento de pacote.

Dois avisos do RustSec ficam ignorados só pelo identificador. O resto do lint continua ligado.

- `RUSTSEC-2024-0370`: `proc-macro-error` 1.0.4, sem upgrade seguro, puxado por `glib-macros` e `gtk3-macros` no Linux.
- `RUSTSEC-2024-0429`: `glib` 0.18.5, unsound em `VariantStrIter`. O conserto publicado é `glib` >=0.20. O Tauri 2.12.3 prende a pilha Linux em `gtk` 0.18 e `webkit2gtk` 2.0.2, que usam `glib` 0.18. O app não chama esse iterador. Trocar a linha do `glib` neste corte quebraria o binding.

O lint de crate não mantido e o de código unsound continuam ligados para o resto do grafo.

Versões duplicadas ficam em aviso. Não são banidas: `getrandom` e outras duplicatas vêm do Tauri e do `ring`, e bani-las não tornaria o grafo único.

## Consequences

- O binário desktop inclui código MPL-2.0. A obrigação de arquivo dessa licença vale para esses crates; não há código MPL escrito neste repositório.
- GPL continua fora. Outra licença só entra depois de um check que a imprima e de uma revisão desta decisão.
- O estágio do produto no README continua o ADR-046. Este ADR não muda esse marcador.
