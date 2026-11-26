# Frontend YoutubeSD (Angular 20)

Fala só com o gateway (http://localhost:8080). O `ng serve` usa `proxy.conf.json` para encaminhar `/api` ao gateway, então não há CORS.

## Rodar

```
cd frontend
npm install
npm start
```

Abra http://localhost:4200 com o ambiente (Aspire ou Compose) já no ar. Requer Node 20.19+ ou 22.12+.

## Teste rápido

1. Crie uma conta e entre; o cabeçalho mostra seu nome.
2. "Enviar vídeo": acompanhe PENDING → PROCESSING → PUBLISHED e a cota (usados / em andamento / disponíveis).
3. "Assistir": escolha o vídeo, troque a qualidade, pause e recarregue a página para ver a retomada.
4. Sem o Upload pronto: `./infra/seed/seed-video.sh demo-1 "Video de demonstracao"` publica um vídeo de exemplo.

## Estrutura

```
src/main.ts, index.html, styles.css
src/app/app.ts, app.config.ts, app.routes.ts
src/app/core/        config (URLs), auth (JWT + refresh), interceptor, guard, api
src/app/pages/       login, watch (catálogo + player HLS + progresso), upload
```

Endpoints usados (todos pelo gateway):

| Tela | Chamada |
|---|---|
| Login | POST /api/auth/login, POST /api/auth/refresh (automático, uma renovação por vez) |
| Cadastro | POST /api/identity/users |
| Cabeçalho / cota | GET /api/identity/users/me, GET /api/identity/quota/me |
| Catálogo / player | GET /api/download/videos, /videos/{id}/playback, manifestos e segmentos HLS |
| Progresso | PUT /api/download/progress/{id}, GET /api/download/progress?status=IN_PROGRESS |
| Upload | POST /api/upload/videos, GET /api/upload/videos?owner=me |

## Pendente de confirmação (Upload)

O Upload não tem README. Confirme no controller e ajuste `src/app/core/api.service.ts` se precisar:
- envio assumido como multipart com os campos `file` e `title`;
- `GET /api/upload/videos?owner=me` assumido como lista com `videoId`, `title` e `status` (se vier como `{ items: [...] }`, ajuste `myUploads()`);
- limite de corpo do YARP para arquivos grandes (combinar com o Integrante 1).

Para falar direto com o gateway sem proxy, troque `gateway` em `src/app/core/config.ts` para `http://localhost:8080` e libere `http://localhost:4200` no CORS do gateway.
