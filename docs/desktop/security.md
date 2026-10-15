# Segurança do cliente Vortex Desktop

Ameaça tratada aqui é a do binário instalado e da WebView. A conta compartilhada sem login é limitação da API e está em `backend-requests.md`. Este documento não a resolve no servidor.

## Superfície

| Bem | Onde vive | Quem pode ler |
| --- | --- | --- |
| URL base | `tauri-plugin-store`, fora da WebView | Núcleo Rust |
| Credencial futura | Keychain (macOS) ou Secret Service (Linux), crate `keyring` 4.2.0, serviço `tech.vortexsoftware.desktop` | Núcleo Rust |
| Snapshot e ordens | Memória do processo e a store Zustand | A WebView local, porque ela desenha a estação |
| CSV | Só o caminho escolhido no diálogo | O operador |
| Log | `app_log_dir`, 7 dias | Quem tem a conta do sistema operacional |
| Chave privada do updater | Secret do CI | Não entra no repositório nem no binário. O binário leva só a chave pública |

Não há banco local, cookie de sessão nem cache de ordem para reenvio.

## CSP

Aplicada ao documento empacotado, sem exceção de desenvolvimento no build de release:

```text
default-src 'self';
script-src 'self';
style-src 'self';
img-src 'self' data:;
font-src 'self';
connect-src 'none';
object-src 'none';
frame-src 'none';
base-uri 'none';
form-action 'none'
```

`connect-src 'none'` é coerente com o HTTP no Rust. A logo da TradingView é arquivo do pacote `lightweight-charts`, copiado para `public/`, não um URL remoto. `tauri.conf.json` deixa `withGlobalTauri` desligado. Não há `dangerousRemoteDomainIpcAccess`. A WebView não navega para fora: `limitNavigationsToAppBoundDomains` no macOS e, nas duas plataformas, cancelamento de navegação cujo esquema não seja `tauri` ou `asset`.

O build de `tauri dev` pode afrouxar `script-src` para o HMR do Vite em `127.0.0.1` somente. Esse afrouxamento não vai para o bundle.

## Capabilities

Uma janela, rótulo `main`. Arquivo `src-tauri/capabilities/default.json`.

Permissão da WebView:

- `core:default` reduzido ao que a janela precisa para existir (mostrar, focar, tema da janela);
- `log:default`.

Fora da capability, mesmo que o plugin esteja linkado:

- `dialog`, `notification`, `store`, `window-state`, `updater`, tray e `single-instance` são chamados pelo Rust;
- `fs`, `shell`, `http`, `global-shortcut`, `process`, `opener` não são dependências.

Os plugins `notification` 2.6.0 e `single-instance` 2.5.3 estão no binário. A capability da WebView não os inclui. O script que o plugin de notificação injeta não recebe permissão de invoke.

Um `invoke` da página só alcança os commands listados em `ipc-contract.md`. Command desconhecido não é registrado. Cada command valida o DTO antes do HTTP.

`set_base_url` recusa URL com credencial embutida, esquema diferente de `http`/`https`, host vazio e qualquer porta fora de produção que não seja a de desenvolvimento explícita `127.0.0.1:5187` ou `localhost:5187`. Apontar o app para outro host é permitido só se o operador editar a config pela tela de conexão, porque o laboratório local precisa disso. A tela mostra o host atual o tempo todo.

## Ações perigosas

Reset zera a conta de todo mundo (ADR-045). Emergency-flat cancela o livro e zera posições. Reverse inverte a posição.

O fluxo é o `DangerGate`:

1. A WebView chama `confirm_danger`.
2. O Rust abre o diálogo nativo, com o verbo e a consequência. Reset diz que a conta de demonstração é compartilhada.
3. O token nasce no processo, não na página. Vale 30 s, uma ação, um uso.
4. `reset_session`, `emergency_flat` e `reverse` sem token, com token gasto ou com token de outra ação devolvem `Danger`.

Atalho dessas três ações, no menu da estação, só abre o diálogo. O token nasce e é gasto no núcleo, e a ação não sai para a API. A boleta continua sendo o caminho que envia. Não há atalho global do sistema operacional.

Flatten de um ativo não pede token: é a ação ordinária da mesa, igual à do web, e não apaga a sessão. Reverse pede, porque o brief trata inverter junto com reset e emergency-flat.

## macOS

Assinatura Developer ID Application, hardened runtime ligado (padrão do Tauri; a documentação de bundle não pede para desligar).

`Entitlements.plist` inicial:

- `com.apple.security.network.client` = true
- `com.apple.security.cs.allow-jit` = true

`allow-jit` é o mínimo conhecido para o JavaScript do WKWebView sob hardened runtime. Não entram `allow-unsigned-executable-memory`, `allow-dyld-environment-variables` nem `disable-library-validation`. A F5 confirma num binário assinado. Se o WebKit recusar a subir sem outra chave, a chave entra neste arquivo com o motivo, não por cópia de tutorial.

Não é distribuição pela Mac App Store. Não há App Sandbox de App Store. Notarização com `notarytool` e staple, como em `packaging-and-ci.md`.

Mínimo do produto: macOS 12. O padrão do Tauri é 10.13. Subimos porque não vamos notarizar nem testar 10.13–11. O `Info.plist` sai com `LSMinimumSystemVersion` 12.0.

## Linux

O `.deb` declara dependência de `libwebkit2gtk-4.1-0`, `libgtk-3-0`, `libayatana-appindicator3-1` e `libsecret-1-0`. O AppImage não embute a glibc. Por isso o build é no Ubuntu 22.04 (glibc 2.35), a base que a documentação do Tauri cita junto com o Debian 12 para WebKitGTK 4.1.

Secret Service ausente não impede o app de operar hoje, porque não há credencial. Quando houver, a falha do `keyring` é erro visível, não fallback para arquivo no disco.

## Modelo de ameaças do cliente

| Ameaça | Efeito | O que o cliente faz |
| --- | --- | --- |
| Página remota carregada na WebView | IPC exposto a terceiro | CSP, sem navegação externa, sem IPC remoto |
| JavaScript lê a URL ou um token futuro | Reuso fora do app | HTTP e Keychain ficam no Rust |
| Replay de POST depois de timeout | Ordem duplicada, API não idempotente | POST não tem retry |
| UI manda reset sem a pessoa ver | Apaga a conta compartilhada | Token do diálogo nativo |
| Decimal vira float no caminho | Saldo deixa de ser o da API | `serde-with-str` e tipo TS `string` |
| Binário apontado para API falsa | Ordens vão para outro host | Host visível na barra. Não há pin de certificado além do que o `rustls` já valida na cadeia do sistema |
| Log vaza ordem | Dados da conta demo no disco | Log de info sem corpo JSON |
| Update trocado | Binário substituído | Manifesto assinado. A chave privada não está no app |
| Segunda instância, dois pollers | Duas boletas na mesma conta | `single-instance` |
| XSS no conteúdo da API | A API manda JSON, não HTML | React escapa texto. Nada de `dangerouslySetInnerHTML` com payload da API |

O que este modelo não segura: qualquer pessoa na internet ainda pode chamar `POST /api/session/reset` sem o app. Isso é o item 1 de `backend-requests.md`.
