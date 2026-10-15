# Empacotamento e CI do Vortex Desktop

Versões fixadas em 2026-10-14. Fonte: https://v2.tauri.app/release/ e as páginas de distribuição citadas no ADR-047.

| Peça | Versão |
| --- | --- |
| `tauri` / `@tauri-apps/api` | 2.12.3 |
| `@tauri-apps/cli` / `tauri-cli` | 2.12.2 |
| `tauri-build` | 2.7.2 |
| `tauri-driver` | 2.1.0 |
| Rust | 1.90.0, MSRV declarado pelo Tauri 2.12.3 |
| Node / npm | 24.x / 11.x, os mesmos do web |
| Plugins | ver tabela em `architecture.md` |

Identificador do bundle: `tech.vortexsoftware.desktop`. Nome do produto: Vortex Desktop.

## Bundles

| Alvo | Obrigatório | Como |
| --- | --- | --- |
| macOS `.app` + `.dmg` universal | Sim | `tauri build --target universal-apple-darwin` em runner macOS |
| Linux AppImage | Sim | `tauri build --bundles appimage` em `ubuntu-22.04` |
| Linux `.deb` | Sim | `tauri build --bundles deb` no mesmo runner |
| Linux `.rpm` | Não | AppImage cobre Fedora sem manter spec de RPM. Reabrir se aparecer base de usuário |
| Flatpak | Não | Outro runtime WebKit e outra revisão de permissão. O `.deb` e o AppImage bastam para o corte |

`bundleMediaFramework` fica desligado. O terminal não toca áudio nem vídeo pelo GStreamer.

### Linux

A documentação do AppImage manda construir na base mais antiga que já tenha `libwebkit2gtk-4.1`. Ubuntu 22.04 é essa base (glibc 2.35). Ubuntu 24.04 no runner levantaria o piso da glibc e o AppImage deixaria de abrir no 22.04.

Distros-alvo do corte:

| Distro | Situação |
| --- | --- |
| Ubuntu 22.04 e 24.04 | Suportadas. 22.04 é a base de build |
| Debian 12 e derivados com WebKitGTK 4.1 no arquivo oficial | Suportadas via `.deb` ou AppImage |
| Linux Mint 21 (base 22.04) | Suportada |
| Fedora com `webkit2gtk4.1` | AppImage, sem `.rpm` neste corte |
| Ubuntu 20.04 e Debian 11 | Fora. Não há WebKitGTK 4.1 no arquivo padrão |

Dependências do `.deb`, alinhadas ao guia de pré-requisitos do Tauri para Debian/Ubuntu:

- `libwebkit2gtk-4.1-0`
- `libgtk-3-0`
- `libayatana-appindicator3-1`
- `librsvg2-2`
- `libsecret-1-0`

O pacote não depende de OpenSSL de sistema. O HTTP usa `rustls`.

Spike obrigatório da F0, antes de tratar o gráfico como pronto: `lightweight-charts` 5.2.1 em WebKitGTK 4.1, X11 e Wayland, GPU Intel, AMD e NVIDIA. Medir FPS com a série de 1 minuto, arrasto de linha de ordem e HiDPI. O contorno já conhecido, `WEBKIT_DISABLE_DMABUF_RENDERER=1`, entra documentado no relatório do spike se a tela preta ou o flicker aparecer. Não entra no launcher como padrão antes dessa medição. A versão mínima de biblioteca é `webkit2gtk-4.1`. Não há fallback para 4.0.

### macOS

- Certificado Developer ID Application. Conta Apple Developer paga. Conta gratuita não notariza.
- Hardened runtime ligado.
- Entitlements: `security.md`.
- `notarytool` pelas variáveis de API da App Store Connect: `APPLE_SIGNING_IDENTITY`, `APPLE_TEAM_ID`, `APPLE_API_KEY`, `APPLE_API_ISSUER`, `APPLE_API_KEY_PATH`.
- Staple no `.dmg`. Sem `--skip-stapling` no artefato publicado.
- Universal: `aarch64` e `x86_64`.
- `minimumSystemVersion`: 12.0. O padrão do Tauri é 10.13; o produto sobe o piso.

## Updater

Escolha: GitHub Releases, plugin `updater` 2.13.3.

O manifesto `latest.json` é um asset do release. O repositório é `joaop-gregorioDS/vortex-trading-terminal`. O endpoint ainda não está no `tauri.conf.json`, porque não há chave de atualização. Quando houver, o endereço é `https://github.com/joaop-gregorioDS/vortex-trading-terminal/releases/latest/download/latest.json`.

A chave pública vai para `tauri.conf.json`. A privada vive só no secret `TAURI_SIGNING_PRIVATE_KEY` e na senha `TAURI_SIGNING_PRIVATE_KEY_PASSWORD`. `cargo-deny` e o guard de segredos do CI continuam proibindo `.env` e chave no Git.

Por que não um arquivo estático na VPS neste corte: publicá-lo exige `location` novo no Nginx, e o brief manda listar mudança de Nginx em `backend-requests.md` em vez de fazê-la. GitHub Releases não mexe na VPS. A alternativa está rascunhada lá, caso o dono não queira depender do GitHub para o binário.

O app avisa e instala pelo menu. Não baixa update no meio de um `submit_order` em voo: `install_update` espera o command de escrita terminar ou o operador confirma que vai interromper.

## CI

Workflow novo: `.github/workflows/desktop.yml`. O `ci.yml` atual não ganha step. Os jobs `backend`, `frontend` e `repository-guards` seguem intocados. Motivo: o guard de estágio procura `ESTAGIO-ATUAL: TASK x.y` e `- Status: Accepted`, e o README de hoje marca `ESTAGIO-ATUAL: ADR-046` com ADRs novos em outro formato. Mexer nesse job neste corte mistura o desktop com um guard que já divergiu do README.

### Pull request: `desktop-check`

Runner: `ubuntu-22.04`. Sem assinatura.

1. Checkout, Rust 1.90.0, Node 24 com cache de npm em `apps/desktop/package-lock.json` e cache de cargo.
2. Apt: `libwebkit2gtk-4.1-dev`, `build-essential`, `libayatana-appindicator3-dev`, `librsvg2-dev`, `patchelf`. Sem `libssl-dev` como dependência do app. O linker do sistema pode precisar de pacotes de build; isso não coloca OpenSSL dentro do HTTP.
3. `npm ci` em `apps/desktop`.
4. `cargo test` em `src-tauri`.
5. Vitest.
6. Teste que regenera os bindings `ts-rs` e falha se o diff não estiver commitado.
7. `cargo deny check`.
8. Não roda `tauri build` em todo PR. O build de bundle fica no workflow de tag, porque WebKitGTK no runner é lento e não prova o macOS.

### Tag `desktop-v*`: `desktop-release`

Matriz:

| Runner | Artefato |
| --- | --- |
| `ubuntu-22.04` | AppImage e `.deb` |
| `macos-latest` | `.dmg` universal notarizado |

`tauri-apps/tauri-action` na versão que acompanhe a CLI 2.12.2 no momento da F5, pinada por SHA no workflow. Secrets só nesse job: assinatura Apple, chave do updater. `permissions: contents: write` só no release. O PR permanece `contents: read`.

O release anexa os bundles e o `latest.json`. Não publica secret. Não altera `deploy/nginx.conf.example`.

### O que não entra no CI

- E2E `tauri-driver` no macOS. O brief registra que o driver não suporta macOS. E2E fica no job Linux, noturno, não bloqueante no PR até a F5 estabilizar. Ver `testing.md`.
- Contrato contra `127.0.0.1:5187` no GitHub. Não há API no runner. Esse teste é local e opcional.

## Licenças

- `lightweight-charts` 5.2.1, Apache-2.0, logo de atribuição obrigatória e local.
- `cargo deny` com `licenses` allow: MIT, Apache-2.0, BSD-3-Clause, ISC, Unicode-3.0, MPL-2.0. MPL entra só se o grafo Rust puxar crate MPL. Se puxar, o ADR de licença registra. GPL não entra sem decisão.
- O check de 2026-10-14 puxou MPL-2.0 (`cssparser`, `selectors`, `option-ext` e vizinhos, via Tauri), `Zlib` (`foldhash`) e `Apache-2.0 WITH LLVM-exception` (`target-lexicon`). ADR-048 registra. O binding do WebKit não foi a origem do MPL.
- npm: o mesmo conjunto, mais a licença do Vite e do React, já compatíveis.
- Marca, layout e texto de Nelogica/Profit não entram no bundle.
