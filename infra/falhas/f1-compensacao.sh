#!/bin/bash
# F1: a dependência do processamento falha 100% → a saga compensa: FAILED, cota devolvida, S3 limpo.
source "$(dirname "$0")/upload-comum.sh"
exigir kafka mysql ministack
[ -n "$(processadores)" ] || { echo "Nenhum media-processor rodando. Suba o AppHost ou o Docker Compose antes."; exit 1; }
trap caos_reset EXIT

titulo "F1 — Dependência falhando 100% → compensação"
echo "Esperado: as tentativas do media-processor falham, o Upload compensa (FAILED), a vaga da cota"
echo "volta para o Identity, o original e o HLS são apagados do S3 e nada é publicado."

passo "Ligando a falha da dependência em 100% (media_db.chaos_settings, todas as réplicas)"
caos NULL 1

passo "Enviando um vídeo de 10 s pelo gateway"
enviar 10 "F1 compensacao"

passo "Tentativas no media-processor"
logs_de media-processor "videoId=$VIDEO_ID" | grep -E "iniciado|falhou|Falha" || true
sql_db media_db "SELECT attempts, status, instance_id, LEFT(last_error, 60) AS ultimo_erro FROM processing_job WHERE video_id = '$VIDEO_ID'"

passo "Saga no upload_db"
estado_video
historico

passo "Cota no identity_db (a reserva precisa ficar RELEASED)"
liberada() { [ "$(sql_db_valor identity_db "SELECT status FROM quota_reservations WHERE video_id = '$VIDEO_ID'")" = "RELEASED" ]; }
aguardar 30 liberada || true
sql_db identity_db "SELECT status, DATE_FORMAT(updated_at, '%T') AS hora FROM quota_reservations WHERE video_id = '$VIDEO_ID'"

passo "Limpeza no S3"
limpo() { [ "$(s3_contar videos-raw "$VIDEO_ID/")" = "0" ] && [ "$(s3_contar videos-hls "$VIDEO_ID/")" = "0" ]; }
aguardar 30 limpo || true
echo "videos-raw/$VIDEO_ID/: $(s3_contar videos-raw "$VIDEO_ID/") objetos, videos-hls/$VIDEO_ID/: $(s3_contar videos-hls "$VIDEO_ID/") objetos"

STATUS=$(sql_db_valor upload_db "SELECT CONCAT(status, '/', IFNULL(failure_reason, '')) FROM video_upload WHERE video_id = '$VIDEO_ID'")
RESERVA=$(sql_db_valor identity_db "SELECT status FROM quota_reservations WHERE video_id = '$VIDEO_ID'")
PUBLICADOS=$(sql_db_valor upload_db "SELECT COUNT(*) FROM outbox WHERE message_key = '$VIDEO_ID' AND type = 'VideoPublished'")

if [ "$STATUS" = "FAILED/PROCESSING_FAILED" ] && [ "$RESERVA" = "RELEASED" ] && [ "$PUBLICADOS" = "0" ] && limpo; then
  resultado ok "compensado: FAILED, cota devolvida, S3 limpo, nenhum VideoPublished."
else
  resultado falhou "status=$STATUS reserva=$RESERVA videoPublished=$PUBLICADOS"
fi
