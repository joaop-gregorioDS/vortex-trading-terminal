# Production deploy — VPS

Artefatos para hospedar o Vortex Trading Terminal na VPS da Vortex Software
(`vortexsoftware.tech`), preservando a stack aprovada: containers Linux, Docker Compose e Nginx.

## Estado real deste deploy

O app funciona de ponta a ponta e **é durável para ordens e posições**: ordens e fills são gravados em
PostgreSQL a cada mutação e restaurados na primeira leitura após um restart (ADR-040). Verificado por
HTTP: encerrar o processo e subir de novo devolve a mesma ordem, com o mesmo status e os mesmos fills,
e a mesma posição, com quantidade, custo e média idênticos.

O que **não** é durável: uma ordem que estava viva no momento da queda volta sem poder ser comandada,
porque recuperar execução em voo exige reconciliação e é decisão pendente. `cash` também é uma
constante de sessão, não um saldo contábil.

O feed de mercado é **simulado e finito**: ele publica preços do gerador determinístico e para quando
a série termina. Não há dado real, fornecedor autorizado ou credencial envolvida.

## Topologia

```text
Internet
   ↓ 443 (TLS no host)
Nginx do host
   ├── /api/  → 127.0.0.1:8080   container api  (ASP.NET Core, ASP.NETCORE_ENVIRONMENT=Production)
   └── /      → 127.0.0.1:3000   container web  (Next.js standalone)
                                     ↓ rede vortex-trading
                                  container postgres (volume nomeado)
```

Os containers da aplicação publicam **apenas em 127.0.0.1**: quem fala com a internet é o Nginx do
host, que já existe para o domínio. Assim não há CORS a configurar, porque navegador e API ficam na
mesma origem, e o banco não fica exposto.

## Arquivos

| Arquivo | Papel |
| --- | --- |
| `deploy/Dockerfile.api` | build e runtime da API em container Linux não-root |
| `deploy/Dockerfile.web` | build e runtime do Next.js em modo standalone |
| `deploy/compose.yaml` | composição de produção: api, web e postgres |
| `deploy/.env.example` | placeholders das três variáveis obrigatórias |
| `deploy/nginx.conf.example` | server block pronto para colar no Nginx do host |

## Passos

Na VPS, com Docker Engine e Compose instalados:

```sh
# 1. código e configuração
git clone <repositório> /srv/vortex-trading
cd /srv/vortex-trading/deploy
cp .env.example .env

# 2. gere uma senha real para o PostgreSQL (não use o placeholder)
openssl rand -hex 32

# 3. edite .env e preencha POSTGRES_PASSWORD, REDIS_PASSWORD (se usar) e WEB_PORT/API_PORT

# 4. valide a composição sem imprimir segredos
docker compose --env-file .env -f compose.yaml config --quiet

# 5. suba
docker compose --env-file .env -f compose.yaml up -d --build

# 6. confirme saúde
curl -fsS http://127.0.0.1:8080/health/ready
curl -fsS http://127.0.0.1:8080/health/live
```

Depois copie `nginx.conf.example` para o Nginx do host, ajuste o nome do servidor e recarregue:

```sh
nginx -t && systemctl reload nginx
# TLS: certbot --nginx -d vortexsoftware.tech
```

## Verificação

```sh
curl -fsS https://vortexsoftware.tech/api/trading/market
curl -fsS https://vortexsoftware.tech/api/portfolio
```

O primeiro deve responder o preço simulado corrente; o segundo, o portfólio com valores em string
decimal.

## Operação

```sh
docker compose --env-file .env -f compose.yaml ps
docker compose --env-file .env -f compose.yaml logs -f api
docker compose --env-file .env -f compose.yaml down          # preserva volumes
```

`down -v` remove o volume do PostgreSQL e destrói os dados. O health check da composição usa
`/health/ready`, que verifica PostgreSQL: enquanto a sessão não usar o banco, um banco fora do ar
ainda derruba o readiness, o que é intencional — significa que a infraestrutura prevista está quebrada,
não que o app parou de funcionar.

## Limites que este deploy não resolve

- Uma ordem viva durante a queda volta sem poder ser comandada: falta recuperação de execução em voo.
- Sem autenticação: qualquer visitante opera a mesma conta simulada.
- Sem multiusuário: uma sessão, uma conta.
- Sem observabilidade: apenas logs do container e os health checks.
- Sem Redis: previsto no ADR-003 para cache e estado efêmero, ainda não usado por caso de uso.

Essas lacunas são decisões pendentes, não detalhes de configuração, e estão registradas nos ADRs
correspondentes.


## Migrations e grafico em Production

Duas coisas mudaram depois da primeira validacao real da stack, e ambas eram falhas que so apareciam
com os containers no ar.

**Migrations.** A API nao aplica migrations no startup, porque a fundacao de persistencia proibe isso,
e a imagem de runtime nao tem SDK nem ferramenta EF. O `Dockerfile.api` tem um estagio `migrations`
que mantem o SDK e restaura a ferramenta pinada, e o Compose o executa como servico de execucao unica
antes da API, exigindo sucesso. Sem isso o schema nao existia, e a primeira submissao de ordem
respondia 500 com `relation "trading.orders" does not exist`.

O alvo de build e **explicito** (`target: runtime`). Um `build` sem alvo pega o ultimo estagio do
arquivo, entao adicionar o estagio de migrations depois do runtime fez o servico `api` executar o
entrypoint de migrations e sair imediatamente — o container entrava em loop de restart.

**Grafico.** O endpoint de demonstracao e registrado apenas em Development, entao a pagina hospedada
mostrava o estado de falha no lugar do grafico: um terminal sem a vitrine visual. A sessao agora
guarda as observacoes que publicou e `GET /api/chart` desenha o historico a partir delas, em todos os
ambientes. Cada observacao vira um candle com o preco exato que a sessao usou para negociar, e nao ha
open/high/low fabricado: os quatro valores sao o preco observado, o que declara o limite em vez de
inventar um range.

**Concorrencia de escrita.** A gravacao de ordens lia as chaves existentes e decidia inserir ou
atualizar. Como o escopo adiciona a ordem ao seu livro em memoria antes de a gravacao terminar, dois
saves concorrentes podiam concluir que a mesma ordem era nova, e o perdedor morria na chave primaria —
a primeira submissao falhava de forma reproduzivel. A gravacao agora toma um lock de sessao do
PostgreSQL (`pg_advisory_lock`) e faz leitura, decisao e escrita atomicamente em relacao aos outros
escritores do schema.

**Durabilidade.** Verificada na stack completa: reiniciar o container da API preserva ordens e
posicoes. Um reinicio, porem, nao recupera uma ordem que estava viva no momento da queda — ela volta
visivel e sem poder ser comandada, porque o workflow exige o estado `Created` e recuperar execucao em
voo e decisao pendente (ADR-040).
