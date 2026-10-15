# Spike WebKit — F0

Data: 2026-10-14. A máquina desta passagem é Windows, com Rust 1.90.0 no crate e WebView2 só para compilar. Os alvos de entrega continuam Linux (WebKitGTK 4.1) e macOS (WKWebView).

## Medido aqui

- `cargo test` em `apps/desktop/src-tauri`: 40 testes passaram, 1 ignorado (`live_terminal_on_local_api`, que só roda com a API em `127.0.0.1:5187`).
- O teste de decimal faz a ida e a volta de `1.000000000000000000000000001` sem notação científica.
- GET sem corpo HTTP tenta 3 vezes. POST tenta 1.
- `{ "status": "empty" }` não engole um snapshot de gráfico.
- `npm test` no front: formatação, cabeçalho sem saldo quando a conexão cai, candles e a ausência de `fetch` para a API.
- `npm run build` gera o `dist` estático.

## Não medido

Nenhum item abaixo está aprovado.

- FPS do `lightweight-charts` 5.2.1 no WebKitGTK 4.1
- X11 e Wayland
- GPU Intel, AMD e NVIDIA
- HiDPI nesses alvos
- Arrasto de linha de preço (esta fatia não desenha linha de ordem)
- WKWebView no macOS 12 ou superior
- `WEBKIT_DISABLE_DMABUF_RENDERER=1`

A variável não entra no launcher. Ela só volta a ser considerada se uma dessas medições mostrar tela preta ou flicker.
