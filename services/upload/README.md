# Upload e media-processor

Envio de vídeos, saga de publicação e transcodificação para HLS do YoutubeSD (C#, .NET 10).

| Projeto | Papel |
|---|---|
| `upload/upload` | API de upload em partes + orquestrador da saga (publica `video.published`) |
| `upload/media-processor` | Worker FFmpeg: `video.uploaded` → HLS 360p/720p em `videos-hls` |
| `upload/upload.messaging` | Base comum: envelope, outbox, consumidor Kafka resiliente, idempotência, EF Core |
| `upload/upload.tests` | Testes da saga, do FFmpeg, dos contratos e do banco (MySQL descartável) |
| `client/youtubesd-upload.js` | Cliente do navegador para o frontend (com `.d.ts`) |

## Upload em partes: o mesmo caminho para vídeo curto e longo

O arquivo vai em partes de 8 MiB pelo gateway (um vídeo curto é uma parte só). A tela nunca fica
travada: criar e concluir respondem `202` na hora, e o processamento acontece em segundo plano.

| Método | Rota | Resposta |
|---|---|---|
| POST | `/api/upload/videos` `{ title, visibility?, fileName, sizeBytes, contentType? }` | `202 { videoId, chunkSize, totalParts, partUrlTemplate, completeUrl, statusUrl }` |
| PUT | `/api/upload/videos/{id}/parts/{n}` (corpo binário, `Content-Length` exato) | `200 { partNumber, sizeBytes, eTag }` |
| POST | `/api/upload/videos/{id}/complete` | `202` com o status |
| GET | `/api/upload/videos/{id}` | status, `receivedParts` (para retomar), prazos, `playbackUrl` quando publicado |
| GET | `/api/upload/videos?owner=me&page=0&size=20` | uploads do usuário |
| DELETE | `/api/upload/videos/{id}` | cancela (antes de publicar) ou remove (publica `video.removed`) |
| GET | `/api/upload/notifications` | notificações do usuário (publicado, falhou, removido) |

Todas exigem JWT; o dono é a claim `sub`. Status públicos: `PENDING`, `PROCESSING`, `PUBLISHED`,
`FAILED`, `REMOVED`.

Cada parte vira um `UploadPart` de um multipart do S3 (`videos-raw/{videoId}/original.{ext}`).
Reenviar a mesma parte só a sobrescreve, então qualquer réplica pode receber qualquer parte e o
cliente pode repetir à vontade.

O `complete` só registra o pedido (`uploadState=COMPLETING`) e responde na hora. Montar o
arquivo no S3 pode levar dezenas de segundos num vídeo grande, então essa montagem é uma tarefa
durável na tabela `storage_cleanup`, executada por qualquer réplica. Se a réplica cair no meio,
outra assume quando a reivindicação expira.

## Saga

```
POST ─► PENDING ─ quota.reserve-requested ─► Identity
           │  (as partes chegam em paralelo à reserva)
           ├─ quota.rejected ──────────────► FAILED (multipart abortado)
           └─ quota.reserved + complete ───► PROCESSING/QUEUED ─ video.uploaded ─► media-processor
                                                │
         video.processing-started ─────────────► RUNNING: prazo = 60 s + 1 s/MB (re-armado a cada tentativa)
         video.processed no prazo ─────────────► PUBLISHED ─ video.published ─► Download + Identity
         video.processing-failed ou prazo ─────► FAILED: quota.release-requested, original e HLS apagados (F1/F7)
         video.processed depois do prazo ──────► HLS apagado, nada publicado (F7)
         na fila além de 30 min (sem processor) ► FAILED (QUEUE_TIMEOUT), cota devolvida
```

Enquanto nenhum media-processor está disponível, o vídeo espera na fila do Kafka. Se a espera passar
de `Upload:Saga:QueueTimeoutMinutes` (30), a saga compensa. As regras ficam em `Saga/VideoSaga.cs`
(sem banco, coberto por `VideoSagaTests`). Cada transição grava estado, `processed_messages`,
`outbox`, limpezas do S3, notificação e histórico num único `SaveChanges`.

## Sistemas distribuídos

| Requisito | Como |
|---|---|
| R1 idempotência | `processed_messages` (mesmo formato do Download) no mesmo `SaveChanges` do efeito; duplicata = chave primária repetida |
| Outbox | `OutboxWriter` grava junto com a transição; `OutboxPublisher` publica. Um líder por banco, eleito por lease em tabela: a ordem por id se mantém e outra réplica assume em até 15 s |
| DLQ | Mensagem inválida: 3 tentativas e depois `<tópico>.dlq` com headers de erro. Dependência fora do ar (MySQL/S3) não vai para a DLQ: espera voltar |
| R4 réplicas | Consumer groups `upload` e `media-processor` (cooperative-sticky). Nenhuma réplica guarda estado em memória |
| R5 dependência instável | `PROCESSOR_LATENCY_MS` e `PROCESSOR_FAILURE_RATE`, sobrepostos ao vivo pela tabela `media_db.chaos_settings` |
| R6 reconexão | Consumidor e produtor reconectam sozinhos. Kafka e S3 não entram no `/health`: uma dependência compartilhada lenta não pode derrubar todas as réplicas do gateway |
| Concorrência entre réplicas | EF Core com concurrency token (`version`): a segunda réplica relê e decide de novo (sem `FOR UPDATE`) |
| Job longo | O consumidor pausa a partição e continua chamando `Consume`: FFmpeg de horas não estoura `max.poll.interval`; se a partição sair da réplica, o job é cancelado e outra assume |

## Escala

- **Horizontal:** `UploadReplicas` e `ProcessorReplicas` no AppHost (2 cada). O YARP distribui as
  partes e tira do ar a réplica que falhar no `/health`.
- **Vertical:** o FFmpeg usa todos os núcleos do container (`Processor:FfmpegThreads=0`).
  `Processor:Concurrency` abre mais jobs por instância (limitado pelas 3 partições do tópico).
  `Upload:MaxConcurrentParts` limita a memória da API (503 + `Retry-After` quando cheia).
  `ProcessorCpus` e `ProcessorMemory` no AppHost mudam `--cpus` e `--memory`.

```
dotnet run --launch-profile http -- --UploadReplicas 3 --ProcessorReplicas 3 --ProcessorCpus 2 --ProcessorMemory 4g
```

## Banco

`upload_db` e `media_db`, com EF Core (provider `MySql.EntityFrameworkCore`). O schema vem de
migrations versionadas, uma por tabela, como as do Download, aplicadas na subida
(`Data/Migrations`). Tabelas do `upload_db`: `video_upload`, `upload_part`, `video_history`,
`user_notification`, `storage_cleanup`, `processed_messages`, `outbox`, `leases`. Tabelas do
`media_db`: `processing_job`, `chaos_settings`, `processed_messages`, `outbox`, `leases`.

Nova migration (na pasta `services/upload/upload/upload`, ou `media-processor`):

```
dotnet tool restore
EF_SCOPE_ENTITIES="VideoUpload,UploadPart,...,NovaEntidade" dotnet dotnet-ef migrations add CreateTableNovaEntidade -o Data/Migrations
```

Sem `EF_SCOPE_ENTITIES`, a migration considera o modelo inteiro.

## Configuração

| Chave | Padrão | Uso |
|---|---|---|
| `ConnectionStrings:uploadDb` / `mediaDb` | localhost | MySQL |
| `Kafka:BootstrapServers` | `localhost:9092` | Kafka (`kafka:9093` entre containers) |
| `S3:ServiceUrl`, `S3:AccessKey`, `S3:SecretKey` | MiniStack local | S3 |
| `Upload:ChunkSizeBytes` | 8388608 | Tamanho da parte |
| `Upload:MaxFileSizeBytes` | 10 GiB | Limite do arquivo |
| `Upload:Saga:QueueTimeoutMinutes` | 30 | Espera máxima na fila sem processor |
| `Upload:Saga:ProcessingBaseSeconds` / `ProcessingSecondsPerMb` | 60 / 1 | Prazo de processamento |
| `Upload:Saga:UploadIdleTimeoutMinutes` | 60 | Envio parado é abandonado |
| `Processor:MaxAttempts` | 3 | Tentativas por vídeo, somadas entre réplicas |
| `PROCESSOR_LATENCY_MS` / `PROCESSOR_FAILURE_RATE` | 0 / 0 | Dependência instável (R5) |

No AppHost: `UploadQueueTimeoutMinutes`, `UploadProcessingBaseSeconds`, `UploadProcessingSecondsPerMb`,
`UploadIdleTimeoutMinutes`, `ProcessorLatencyMs`, `ProcessorFailureRate`.

## Scripts

```
./infra/upload/enviar-video.sh --gerar 20 "Meu teste"          # vídeo de teste de 20 s
./infra/upload/enviar-video.sh C:/videos/aula.mp4 "Aula 1"     # arquivo seu
RETOMAR=<videoId> ./infra/upload/enviar-video.sh C:/videos/aula.mp4   # retoma um envio interrompido
./infra/falhas/f1-compensacao.sh    # dependência 100% falhando → compensação
./infra/falhas/f2-processor.sh      # docker kill no processor no meio do job
./infra/falhas/f7-prazo.sh          # resultado depois do prazo
```

O `enviar-video.sh` mostra qual réplica recebeu cada parte. Com `docker kill` numa réplica do
Upload durante o envio, as partes seguintes vão para a outra. Sem nenhuma réplica, o script espera
e continua quando alguma volta.

Os vídeos de teste usam a cota do usuário `demo-upload@youtubesd.local` (10 vagas). Para zerar:
`IDENTITY_ADMIN_TOKEN=... ./services/identity/scripts/f5-cota.sh` ou o endpoint de reset do Identity.

## Testes

```
dotnet test services/upload/upload/upload.tests
```

`DatabaseTests` sobe um MySQL descartável (precisa do Docker). Os outros testes rodam sem nada.
