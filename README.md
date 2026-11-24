# YoutubeSD

Plataforma de vídeos sob demanda com arquitetura de microsserviços.
Trabalho final de Sistemas Distribuídos — FURB.

## Estrutura

```
Orchestrador/YoutubeOrchestrador/
  YoutubeOrchestrador.AppHost/          orquestração com .NET Aspire
  YoutubeOrchestrador.Gateway/          gateway YARP (porta 8080)
  YoutubeOrchestrador.ServiceDefaults/  health check e telemetria dos serviços .NET
services/
  security/   autenticação, JWT RS256, JWKS e renovação de sessões (C#)
  download/   reprodução dos vídeos (Java, Spring Boot)
  identity/   usuários e cota de uploads (C#)
  upload/     envio e processamento dos vídeos (C#)
contracts/    contratos das mensagens (JSON Schema)
infra/
  kafka/      criação dos tópicos
  ministack/  criação dos buckets do S3
  seed/       vídeo de exemplo
  falhas/     roteiro dos cenários de falha
deploy/       Docker Compose gerado a partir do AppHost
```

## Pré-requisitos

- Docker Desktop aberto.
- .NET SDK 10, para subir pelo Aspire.
- Java 25, apenas para rodar os testes do Download fora do Docker.
- Git Bash, para os scripts de `infra/`.

## Subir pelo Aspire

Na raiz do repositório, prepare uma vez a chave RSA e os segredos locais:

```
dotnet run --project services/security/tools/SecuritySetup
```

O comando preserva valores existentes em `.secrets/security.json` e `deploy/.env`, ambos ignorados pelo Git. Não remova esses arquivos entre reinícios: todas as réplicas precisam compartilhar a mesma chave.

```
cd Orchestrador/YoutubeOrchestrador/YoutubeOrchestrador.AppHost
dotnet run --launch-profile http
```

O dashboard abre em http://localhost:15006. O número de réplicas do Download é configurável:

```
dotnet run --launch-profile http -- --DownloadReplicas 1
```

## Subir pelo Docker Compose

Execute a preparação acima antes da primeira subida.

```
cd deploy
docker compose up --build -d
```

Para parar: `docker compose down`. Os dados ficam nos volumes.
O dashboard do Aspire fica em http://localhost:18888; o token de acesso aparece em `docker compose logs compose-dashboard`.

Para regenerar o `docker-compose.yaml` depois de alterar o AppHost, na pasta do AppHost:

```
dotnet dnx --yes aspire.cli@13.6.5 -- publish -o ../../../deploy --non-interactive
```

## Um ambiente de cada vez

Aspire e Compose usam as mesmas portas. Antes de trocar:

- do Compose para o Aspire: `docker compose down` em `deploy/`;
- do Aspire para o Compose: pare o AppHost e os containers persistentes dele (`redis`, `kafka`, `kafka-ui`, `mysql` e `ministack`).

## Endereços

- Gateway: http://localhost:8080
- Download (réplicas): http://localhost:8401, 8402 e 8403
- Swagger do Download: http://localhost:8401/swagger-ui/index.html
- Kafka UI: http://localhost:8090

## Vídeo de exemplo

Com o ambiente no ar, na raiz do repositório:

```
./infra/seed/seed-video.sh demo-1 "Video de demonstracao"
./infra/seed/seed-video.sh meu-video "Meu video" "C:/caminho/para/arquivo.mp4"
```

O script gera o HLS (360p e 720p) com FFmpeg num container, envia ao S3 e publica o `VideoPublished`.

## Cenários de falha

Com o ambiente no ar e as 3 réplicas do Download, na raiz do repositório:

```
./infra/falhas/f2-kill-replica.sh
./infra/falhas/f3-duplicada.sh
./infra/falhas/f4-venenosa.sh
./infra/falhas/f6-kafka-fora.sh
./infra/falhas/f8-replicas.sh
./infra/falhas/limpar.sh
```

Para o F5, configure `IDENTITY_ADMIN_TOKEN` com o valor local de `deploy/.env` e execute `./services/identity/scripts/f5-cota.sh`. O reset de cota exige esse segredo.

## Autenticação e testes do Security

Cadastre um usuário em `POST /api/identity/users` (`name`, `email`, `password`) e faça login em `POST /api/auth/login` (`email`, `password`). Use `accessToken` como `Authorization: Bearer <token>` nas rotas pessoais do Identity e Download. `X-User-Id` não autentica usuários no ambiente integrado. A renovação é `POST /api/auth/refresh` com `refreshToken` e devolve um novo par de tokens; cada refresh token só pode ser usado uma vez.

```
dotnet test services/security/tests/Security.Tests.csproj
./services/security/scripts/smoke-auth.sh
```

O primeiro comando usa um MySQL descartável e não depende do ambiente completo. O segundo precisa do Compose ou Aspire no ar e valida cadastro, login, JWT entre C# e Java, perfil, cota, progresso e renovação pelo gateway. Consulte `services/security/README.md` para configuração, persistência e comportamento sob falha.

As rotas `/api/identity/internal/**` são bloqueadas no gateway. O cadastro permanece público; perfil, cota e progresso exigem JWT. O JWKS público está em `GET /api/auth/.well-known/jwks.json`.

Cada script provoca a falha, mostra o estado antes e depois e termina com `RESULTADO: OK` ou `FALHOU`.
O `limpar.sh` remove os dados de teste do catálogo.

## Testes do Download

Com o Docker aberto, em `services/download`:

```
./mvnw verify
```
