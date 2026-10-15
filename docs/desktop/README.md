# Documentos do Vortex Desktop

ADR-047 aceito. O app está em `apps/desktop`. O corte da F5 no repositório é o `cargo deny` e o `desktop.yml`. O corte da F6 é o menu da estação, o aviso de execução e a segunda instância. A barra de menu, a notificação do sistema e um segundo processo não foram vistos. AppImage numa instalação limpa, `.dmg` notarizado e updater seguem pendentes.

| Documento | Conteúdo |
| --- | --- |
| [ADR-047](../adr/ADR-047-desktop-tauri-clients.md) | Decisão: clientes Linux e macOS em Tauri 2 |
| [ADR-048](../adr/ADR-048-desktop-licenses.md) | Licenças que o `cargo deny` do desktop aceita |
| [architecture.md](architecture.md) | Componentes, sequências, pastas, telas, decisões |
| [ipc-contract.md](ipc-contract.md) | Commands, events, timeout, mensagens |
| [security.md](security.md) | CSP, capabilities, entitlements, ameaças |
| [packaging-and-ci.md](packaging-and-ci.md) | Bundles, assinatura, updater, CI |
| [backend-requests.md](backend-requests.md) | O que a API ainda precisaria, sem implementar |
| [phases.md](phases.md) | F0 a F6 e o aceite de cada uma |
| [testing.md](testing.md) | Rust, Vitest, bindings, E2E Linux |
