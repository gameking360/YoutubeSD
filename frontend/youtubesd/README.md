# Frontend YoutubeSD (Angular 20)

Interface web do YoutubeSD. Fala só com o gateway, pelo caminho `/api`: no Docker Compose e no
Aspire, o nginx da imagem repassa `/api` ao gateway; no `ng serve`, o `proxy.conf.mjs` faz o
mesmo. Como tudo sai da mesma origem, não há CORS.

## Rodar

Com o sistema no ar pelo Docker Compose ou pelo Aspire, o front já sobe junto em
**http://localhost:4200**.

Para desenvolver com recarga automática, pare o container `frontend` (a porta é a mesma) e rode:

```
cd frontend/youtubesd
npm install
npm start
```

Requer Node 20.19+ ou 22.12+.

## Teste rápido

1. Crie uma conta e entre; o cabeçalho mostra seu nome.
2. "Enviar vídeo": acompanhe PENDING → PROCESSING → PUBLISHED, a cota e as notificações.
3. "Assistir": escolha o vídeo, troque a qualidade, pause e recarregue a página para ver a retomada.
4. "Perfil": dados da conta, troca de nome e cota.

## Estrutura

```
Dockerfile, nginx.conf.template   imagem do front (build do Angular servido pelo nginx)
proxy.conf.mjs                    proxy do ng serve para o gateway
src/app/core/                     config (URLs), auth (JWT + refresh), interceptor, guard, api
src/app/pages/                    login, watch (catálogo, player HLS e progresso), upload, profile
```

Endpoints usados (todos pelo gateway):

| Tela | Chamada |
|---|---|
| Login | POST /api/auth/login, POST /api/auth/refresh (automático, uma renovação por vez) |
| Cadastro | POST /api/identity/users |
| Cabeçalho / perfil / cota | GET e PUT /api/identity/users/me, GET /api/identity/quota/me |
| Catálogo / player | GET /api/download/videos, /videos/{id}/playback, manifestos e segmentos HLS |
| Progresso | PUT /api/download/progress/{id}, GET /api/download/progress?status=IN_PROGRESS |
| Upload | POST /api/upload/videos, PUT /api/upload/videos/{id}/parts/{n}, POST /api/upload/videos/{id}/complete, GET /api/upload/videos?owner=me, GET /api/upload/notifications |
