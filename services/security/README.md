# Security

Serviço de autenticação do YoutubeSD, em ASP.NET Core / .NET 10. O Identity mantém usuários e hashes de senha; o Security mantém sessões em `security_db` e assina JWTs RS256.

## Preparar e executar

Com o Docker Desktop aberto, suba pelo **Docker Compose** a partir da raiz do repositório.

```sh
docker compose --project-directory deploy up --build -d
```

O `security-init` gera uma chave RSA de 3072 bits, a chave interna e o token administrativo. Se houver valores em `deploy/.env` na primeira subida, ele os aproveita. As credenciais persistem nos volumes do Docker entre reinícios e recriações dos contêineres.

Para subir pelo **Aspire**, execute na raiz do repositório os comandos abaixo.

```sh
dotnet run --project services/security/tools/SecuritySetup
cd Orchestrador/YoutubeOrchestrador/YoutubeOrchestrador.AppHost
dotnet run --launch-profile http
```

O `SecuritySetup` gera uma chave RSA de 3072 bits e segredos locais em `.secrets/security.json` e `deploy/.env` (ignorados pelo Git e pelos builds Docker). Executá-lo novamente preserva os valores. Todas as réplicas recebem a mesma chave via configuração. Substituir a chave invalida tokens já emitidos.

O `SecuritySetup` é uma ferramenta de preparação do ambiente local. Sua execução é manual e ele não é incluído na publicação da API nem executado na inicialização do serviço. Em produção, a chave de assinatura e os segredos devem ser provisionados pela infraestrutura e fornecidos pelas configurações documentadas abaixo, compartilhando a mesma chave entre as réplicas. O serviço exige esses valores e não gera uma chave automaticamente.

## API pelo gateway

| Método | Caminho | Corpo / resultado |
|---|---|---|
| POST | `/api/auth/login` | `{ "email", "password" }` → `{ accessToken, refreshToken, expiresIn }` |
| POST | `/api/auth/refresh` | `{ "refreshToken" }` → novo par de tokens |
| GET | `/api/auth/.well-known/jwks.json` | Chave RSA pública (`kid`, `n`, `e`, `alg`, `kty`, `use`) |

Primeiro cadastre o usuário em `POST /api/identity/users` com `{ "name", "email", "password" }`. Envie o access token nas APIs pessoais como `Authorization: Bearer <accessToken>`. `expiresIn` é expresso em segundos (900 por padrão). Credenciais inválidas e sessões inválidas retornam 401, entradas malformadas 400 e indisponibilidade de Identity/MySQL 503. Respostas contendo tokens usam `Cache-Control: no-store`.

O JWT contém `sub` (ID do Identity), `roles` (lista), `exp`, `iat`, `nbf`, `iss`, `aud` e `jti`, com `kid` no cabeçalho. A chave privada nunca aparece no JWKS. Serviços validam assinatura, algoritmo, emissor, audiência e expiração localmente. A consulta JWKS usa cache: uma chave já conhecida continua permitindo validação durante indisponibilidade temporária do Security; a primeira carga depende do endpoint.

## Sessões e concorrência

`auth_sessions` registra usuário, criação, expiração absoluta e revogação. `refresh_tokens` contém somente hashes SHA-256 de tokens aleatórios de 384 bits, uso e substituição. Não há acesso direto ao banco do Identity nem foreign key entre serviços.

Cada refresh verifica o usuário no Identity e atualiza seus papéis. A troca dos tokens ocorre numa transação que bloqueia primeiro a sessão. Uma renovação concorrente/repetida não gera duas substituições: a reutilização revoga toda a sessão, inclusive o refresh mais recente. Clientes devem serializar renovações e fazer novo login se perderem a resposta de uma renovação. Uma falha do Identity anterior à troca não consome o token.

O prazo da sessão é absoluto: renovar não prolonga os sete dias originais. Revogar a sessão impede novas renovações; JWTs já emitidos permanecem válidos até expirar. O histórico dos tokens é conservado para detectar reutilização; limpeza periódica de sessões expiradas pode ser acrescentada quando houver necessidade operacional.

## Configuração

| Variável | Uso / padrão |
|---|---|
| `ConnectionStrings__securityDb` | Conexão MySQL, injetada pelo Aspire |
| `Security__SigningKeyBase64` | Obrigatória; chave privada RSA PKCS#8 codificada em Base64 |
| `Security__IdentityInternalApiKey` | Obrigatória; mesmo valor de `Identity__InternalApiKey` |
| `Security__IdentityBaseUrl` | `http://identityService`, resolvido por service discovery |
| `Security__IdentityTimeoutSeconds` | 5 |
| `Security__Issuer` | `youtubesd-security` |
| `Security__Audience` | `youtubesd-api` |
| `Security__AccessTokenMinutes` | 15 |
| `Security__RefreshTokenDays` | 7 |
| `INSTANCE_ID` | Identificação da instância nos logs; padrão: nome da máquina |

No AppHost, os parâmetros secretos são `Parameters__security-signing-key`, `Parameters__identity-internal-key` e `Parameters__identity-admin-token`. A configuração local gerada é opcional se esses parâmetros forem fornecidos externamente. No Compose, os nomes estão em `deploy/.env.example`. HTTP sem TLS é usado exclusivamente no ambiente local emulado; fora dele configure transporte HTTPS e `Jwt__RequireHttpsMetadata=true` nos consumidores .NET.

Logs incluem usuário/sessão quando disponíveis, correlação e instância. Senhas, access tokens e refresh tokens não são registrados. Security não produz nem consome eventos Kafka; não depende de outbox ou DLQ.

## Testes

```sh
dotnet test services/security/tests/Security.Tests.csproj
./services/security/scripts/smoke-auth.sh
# Windows / PowerShell, com o Compose no ar:
powershell -NoProfile -ExecutionPolicy Bypass -File services/security/scripts/validate-compose.ps1
```

Os testes automatizados criam um MySQL descartável com Testcontainers e simulam o Identity. Cobrem assinatura, claims, erros, rotação, expiração, reutilização, múltiplas instâncias e concorrência real no MySQL. O smoke usa o ambiente completo pelo gateway e demonstra a validação do token C# no Download Java. Não imprime tokens; deixa um usuário e um progresso de teste identificados por `security-smoke`.

O reset administrativo da demonstração F5 exige `IDENTITY_ADMIN_TOKEN`. As APIs `/api/identity/internal/**` são bloqueadas no gateway e exigem a chave interna quando acessadas diretamente. [Como obter o token administrativo](../../docs/COMO-INICIAR.md#token-administrativo)

`validate-compose.ps1` reinicia o Security, verifica a persistência da chave/sessão, para o Security para testar validação com JWKS em cache, interrompe o Identity para conferir 503 e recuperação sem consumir refresh, e envia 12 renovações simultâneas pelo gateway. Restaura os serviços no final e não imprime segredos.

Em redes com inspeção TLS, os Dockerfiles aceitam o segredo opcional de build `build_ca` com a CA confiável da organização. Ele é usado somente no estágio de compilação; a verificação TLS permanece ativa. Não é necessário em redes sem inspeção.
