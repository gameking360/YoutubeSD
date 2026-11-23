# Identity

Microsserviço de usuários e de cota de uploads do YoutubeSD (C#, .NET 10, ASP.NET Core).

## O que entrega

- Cadastro, consulta e atualização de perfil de usuário.
- Validação de credenciais para o Security.
- Dono da cota de uploads (recurso escasso, R2).
- Consumidor Kafka idempotente, com outbox, DLQ e reconexão.
- Endpoints de health (`/health` e `/alive`) fornecidos pelo `ServiceDefaults`.

## API

Prefixo mantido pelo serviço: `/api/identity`. O gateway encaminha `/api/identity/**` para cá.

| Método | Rota | Descrição |
|---|---|---|
| POST | `/api/identity/users` | Cadastro: `{ "name", "email", "password" }` |
| GET | `/api/identity/users/{id}` | Perfil do usuário |
| GET | `/api/identity/users/me` | Perfil do usuário do header `X-User-Id` |
| PUT | `/api/identity/users/me` | Atualiza o nome: `{ "name" }` |
| GET | `/api/identity/quota/me` | Cota do usuário: `{ limit, used, reserved, available }` |
| POST | `/api/identity/internal/authenticate` | `{ "email", "password" }` → dados do usuário; usado pelo Security |
| POST | `/api/identity/admin/users/{id}/quota/reset` | Zera `used`/`reserved` e as reservas (demonstração F5) |

`X-User-Id` é o mesmo header usado pelo Download enquanto o Security não entra. A validação local do JWT (JWKS) fica para a integração com o Security; o código já lê a claim `sub` se ela estiver disponível no `HttpContext`. Os endpoints `internal` e `admin` podem ser protegidos por `Identity:InternalApiKey` e `Identity:AdminToken`; sem essas variáveis, ficam abertos (adequado só ao protótipo).

## Eventos

Consome:

| Tópico | Tipo | Contrato |
|---|---|---|
| `quota.reserve-requested` | `QuotaReserveRequested` | `contracts/quota-reserve-requested.v1.schema.json` |
| `quota.release-requested` | `QuotaReleaseRequested` | `contracts/quota-release-requested.v1.schema.json` |
| `video.published` | `VideoPublished` | `contracts/video-published.v1.schema.json` |

Produz (sempre pelo outbox, chave = `videoId`):

| Tópico | Tipo | Contrato |
|---|---|---|
| `quota.reserved` | `QuotaReserved` | `contracts/quota-reserved.v1.schema.json` |
| `quota.rejected` | `QuotaRejected` | `contracts/quota-rejected.v1.schema.json` |

Fluxo da cota:

1. `quota.reserve-requested` → reserva atômica de uma vaga → publica `quota.reserved` ou `quota.rejected`.
2. `quota.release-requested` → devolve a vaga (se a reserva já foi confirmada, devolve o uso).
3. `video.published` → converte a reserva em uso confirmado (`reserved--`, `used++`).

O limite padrão é 10 por usuário (`Identity:DefaultQuotaLimit`). A reserva usa:

```sql
UPDATE quotas
SET reserved = reserved + 1
WHERE user_id = ? AND used + reserved < quota_limit
```

A linha afetada é verificada dentro da mesma transação que grava `processed_messages`, a reserva e o evento no `outbox`. É isso que garante F5: 100 pedidos para 10 vagas resultam em exatamente 10 aceitos.

## Modelo de dados (`identity_db`)

| Tabela | Papel |
|---|---|
| `users` | id, nome, e-mail único, hash PBKDF2 da senha, papel |
| `quotas` | limite, usados e reservados por usuário |
| `quota_reservations` | uma reserva por `video_id` (status `RESERVED`, `CONFIRMED`, `RELEASED`) |
| `processed_messages` | idempotência dos consumidores (R1) |
| `outbox` | eventos pendentes de publicação, na mesma transação do efeito |

O schema é criado no start (`Data/Schema.sql`, `CREATE TABLE IF NOT EXISTS`). Se o database `identity_db` ainda não existir, o próprio serviço o cria antes de aplicar o schema.

## Confiabilidade

- **R1**: `processed_messages` gravado na mesma transação do efeito; mensagem repetida gera log `Mensagem duplicada ignorada`.
- **Outbox**: produtor grava no `outbox` na transação; `OutboxPublisher` publica e marca `published_at`.
- **DLQ**: após `Kafka:MaxAttempts` tentativas, a mensagem vai para `<tópico>.dlq` com headers de erro e o offset é confirmado.
- **R4**: consumer group `identity` (configurável), sem auto-commit, commit manual após o efeito.
- **R6**: erros de Kafka no consumidor não derrubam o serviço; o outbox fica pendente e drena quando o Kafka volta.
- **Logs**: `messageId`, `correlationId`, `videoId` e `instancia` nos efeitos e nas duplicatas.

## Configuração

| Variável | Padrão | Uso |
|---|---|---|
| `ConnectionStrings__identityDb` | — | conexão MySQL; o Aspire injeta |
| `ConnectionStrings__kafka` ou `KAFKA_BOOTSTRAP_SERVERS` | `localhost:9092` | brokers |
| `Kafka__ConsumerGroup` | `identity` | grupo dos consumidores |
| `Kafka__MaxAttempts` | `3` | tentativas antes da DLQ |
| `Kafka__Topics__*` | nomes do `appsettings.json` | tópicos |
| `Identity__DefaultQuotaLimit` | `10` | vagas por usuário |
| `Identity__AdminToken` | vazio | protege o reset de cota |
| `Identity__InternalApiKey` | vazio | protege a validação de credenciais |
| `INSTANCE_ID` | nome da máquina | identificação nos logs |

## Como rodar

Com o Docker Desktop aberto, na raiz do repositório:

```
cd Orchestrador/YoutubeOrchestrador/YoutubeOrchestrador.AppHost
dotnet run --launch-profile http
```

O Identity sobe como `identityService`, com o banco `identity_db` e o Kafka injetados pelo AppHost. No gateway: `http://localhost:8080/api/identity/...`.

Exemplo:

```
curl -s -X POST http://localhost:8080/api/identity/users \
  -H 'Content-Type: application/json' \
  -d '{"name":"Ana","email":"ana@teste.local","password":"senha123"}'
```

A imagem Docker usa a raiz do repositório como contexto:

```
docker build -f services/identity/Dockerfile -t youtubesd-identity .
```

## F5

Com o ambiente no ar e o Identity consumindo o Kafka:

```
./services/identity/scripts/f5-cota.sh
```

O script cria um usuário, publica 100 pedidos de reserva e confere no banco 10 reservas ativas e 90 rejeições.

## Dependências de outras partes

- **Upload**: precisa publicar `quota.reserve-requested` e `quota.release-requested` no formato dos contratos acima e consumir `quota.reserved`/`quota.rejected`. Hoje o Upload ainda é o projeto vazio; até lá o script F5 publica as mensagens manualmente.
- **Security**: deve chamar `POST /api/identity/internal/authenticate` para validar e-mail/senha e emitir o JWT com `sub` = `users.id`. O endpoint de login continua sendo do Security.
- **Gateway**: rota `/api/identity/**` já configurada em `YoutubeOrchestrador.Gateway/appsettings.json`.
- **Compose**: o `deploy/docker-compose.yaml` precisa ser regerado a partir do AppHost para incluir o Identity.
