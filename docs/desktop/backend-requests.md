# Pedidos ao backend

Nenhum item deste arquivo está implementado. Nenhum command do desktop depende deles para a F0–F4. São o que a API ainda não oferece e o que o cliente não deve inventar.

Cada rascunho abaixo seria um ADR novo, em `docs/adr/`, no formato com `- Status:`. Números sugeridos, não reservados: 048 em diante. Quem aceitar escolhe o número livre no dia.

## 1. Autenticação da conta compartilhada

Hoje qualquer cliente alcança `POST /api/orders` e `POST /api/session/reset`. O ADR-039 e o ADR-045 registram a conta única de propósito. O desktop só acrescenta confirmação nativa. Isso não impede um `curl`.

| Opção | O que muda | Custo | O que não resolve |
| --- | --- | --- | --- |
| Rate limit no Nginx | `limit_req` em `POST /api/session/reset` e `POST /api/orders` | Baixo. Só `deploy/nginx.conf.example` e o host | Um cliente lento ainda reseta a conta |
| API key por instalação | Header `X-Vortex-Key`. A chave nasce no primeiro launch, vai para o `keyring`, e o Nginx ou a API recusa sem ela | Médio. Emissão, revogação, e o app passa a ter segredo | Continua uma conta só, se a chave não virar dono de sessão |
| Token de dispositivo | `POST /api/devices` devolve um token opaco guardado no `keyring` | Médio, parecido com a API key, com endpoint novo | Igual, enquanto o token não seleciona uma conta |
| OIDC | Login, conta por pessoa, o reset deixa de ser global | Alto. Novo módulo Authentication, que a visão arquitetural já lista como futuro, e revisão do ADR-039 | — |

Recomendação: rate limit agora, como mitigação barata, e API key como primeiro ADR se o dono quiser fechar o reset anônimo sem abrir mão da conta única. OIDC só quando existir conta por operador. O desktop já tem o lugar da credencial (`keyring`) e não vai gravar chave em arquivo.

### Rascunho ADR — Rate limit na borda

- Status: Proposed
- Contexto: a conta de demonstração é pública. O reset é global.
- Decisão: Nginx `limit_req` na zona de `POST /api/session/reset` (por exemplo 1 pedido a cada 10 s por IP) e um teto mais alto em `POST /api/orders`. A API não muda.
- Consequências: abuso casual cai. IP compartilhado pode afetar uma turma. Não é autenticação.

### Rascunho ADR — API key de instalação

- Status: Proposed
- Contexto: o rate limit não identifica o app.
- Decisão: a API exige `X-Vortex-Key` nos POST de escrita. A chave é um segredo opaco por instalação, não um usuário. Sem chave, 401 ProblemDetails com `code: client_unauthorized`.
- Consequências: o web público também precisa da chave ou de uma isenção explícita. Isentar o web mantém o buraco. Exigir nos dois fecha o `curl` e obriga a distribuir a chave. A conta segue única.

## 2. Versão do contrato

A API rejeita propriedade desconhecida no request (`UnmappedMemberHandling.Disallow` em `TradingEndpoints`). Um desktop antigo que mande um campo novo quebra. Um desktop novo que exija um campo que o servidor ainda não manda cai no deserializar tolerante e pode seguir sem esse dado.

Proposta, sem implementar:

- Todo cliente já mandará `X-Vortex-Client: vortex-desktop/<versão>`. A API de hoje ignora header desconhecido. Pode passar a registrar.
- `GET /api/status` ganha dois campos opcionais: `apiVersion` e `minClientVersion`. Cliente antigo ignora, porque a response não usa deny.
- Se `minClientVersion` vier e for maior que a versão do app, a UI bloqueia a boleta e oferece o updater. Leitura do snapshot pode continuar.
- Enquanto os campos não existirem, incompatibilidade prática é HTTP 400 `invalid_request` num payload que o app sabe que é válido: a UI mostra "Atualize o Vortex Desktop" e o log guarda `traceId`.

### Rascunho ADR — Versão mínima de cliente

- Status: Proposed
- Contexto: o binário instalado sobrevive a deploys da API.
- Decisão: `StatusResponse` ganha `apiVersion` e `minClientVersion` opcionais. POST desconhecido continua 400. O cliente trata `minClientVersion` como trava de escrita.
- Consequências: o web ignora os campos. Não há segundo contrato.

## 3. OpenAPI no CI, não na produção

Swagger está atrás de `IsDevelopment()` em `Program.cs`. Produção não publica `/swagger`. Está certo para a VPS.

Pedido: o job de backend já gera ou passa a arquivar o `swagger.json` de Development como artefato do CI. O desktop compara as fixtures do `wiremock` com esse documento. Divergência falha o PR do desktop. Não abrir Swagger no Nginx.

### Rascunho ADR — OpenAPI como artefato de CI

- Status: Proposed
- Decisão: `dotnet swagger` no job backend, artifact `openapi.json`, sem endpoint novo em Production.
- Consequências: o contrato viaja com o commit. A VPS não ganha superfície de documentação.

## 4. Push

Não há push. O poller é a v1, atrás da trait `SnapshotSource`.

| Opção | Para o desktop | Para o web |
| --- | --- | --- |
| SSE de `GET /api/terminal` | Um `reqwest` stream troca o poller | O browser também sabe ler SSE |
| SignalR | Exige cliente Rust de terceiros. A visão e o ADR-041 citam SignalR como evolução do browser | Encaixa no que já está escrito |

Recomendação: um canal só. Se o dono for implementar push, SSE do mesmo JSON do snapshot evita dois protocolos. SignalR permanece a alternativa se a prioridade for o web. O desktop não implementa nenhum dos dois até o ADR existir. O poller fica.

### Rascunho ADR — Canal único de snapshot

- Status: Proposed
- Decisão: adiar. Quando houver push, um endpoint só, o mesmo documento de `GET /api/terminal`, com id de geração. O cliente descarta geração velha, como o poller já faz.
- Consequências: o ADR escolhe SSE ou SignalR nesse dia. Não os dois.

## 5. Manifesto do updater na VPS

Alternativa recusada para a v1, registrada porque muda o Nginx.

`location /desktop/updates/` serviria `latest.json` e os bundles a partir de um diretório no host, ainda atrás do HTTPS do certbot, com `client_max_body_size` inalterado (o upload não passa pela API). O app usaria `https://vortexsoftware.tech/desktop/updates/latest.json`.

Custo: operar disco e permissão no host, e publicar binário na mesma máquina da API. Benefício: o usuário não depende do GitHub. Fica como opção se o ADR-047 trocar a decisão do updater.

### Rascunho ADR — Updates estáticos

- Status: Proposed
- Decisão: não fazer enquanto GitHub Releases atender. Se fizer, só arquivo estático no Nginx, sem endpoint na API .NET.
- Consequências: o `deploy/nginx.conf.example` ganha um `location`. A API continua sem conhecer o binário.

## 6. O que não é pedido

- Endpoint novo de P&L. O snapshot já traz os números.
- Book de cinco níveis. O ADR-045 diz que o livro são as ordens em repouso, não uma escada inventada.
- Retry idempotente de ordem. Exigiria chave de idempotência na API. Fora deste corte. O cliente não repete POST.
