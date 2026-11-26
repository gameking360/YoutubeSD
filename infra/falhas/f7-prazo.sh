#!/bin/bash
# F7: o resultado do processamento chega depois do prazo → compensação, e o resultado tardio é descartado.
# O prazo é Upload:Saga:ProcessingBaseSeconds (60 s) + 1 s por MB; PRAZO_BASE ajusta se mudar a base.
source "$(dirname "$0")/upload-comum.sh"
exigir kafka mysql ministack
[ -n "$(processadores)" ] || { echo "Nenhum media-processor rodando. Suba o AppHost ou o Docker Compose antes."; exit 1; }
trap caos_reset EXIT

PRAZO_BASE="${PRAZO_BASE:-60}"
LATENCIA_MS=$(( (PRAZO_BASE + 40) * 1000 ))

titulo "F7 — Resultado depois do prazo → compensação"
echo "Esperado: o prazo vence com o processamento ainda em andamento → FAILED (DEADLINE_EXPIRED) e"
echo "cota devolvida. Quando o video.processed chega atrasado, o HLS é apagado e nada é publicado."

passo "Deixando a dependência lenta: ${LATENCIA_MS}ms por tentativa (prazo ≈ ${PRAZO_BASE}s + 1s/MB)"
caos "$LATENCIA_MS" NULL

passo "Enviando um vídeo de 10 s pelo gateway (espera o prazo vencer)"
enviar 10 "F7 prazo"

passo "Aguardando o resultado tardio do media-processor"
tardio() { [ "$(sql_db_valor upload_db "SELECT COUNT(*) FROM video_history WHERE video_id = '$VIDEO_ID' AND reason = 'LATE_RESULT_DISCARDED'")" -ge 1 ]; }
aguardar 150 tardio || true
logs_de upload "videoId=$VIDEO_ID" | grep -E "VideoProcessed|TimeoutCheck|Limpeza" || true

passo "Saga no upload_db"
estado_video
historico

passo "HLS gerado atrasado precisa ser apagado"
sem_hls() { [ "$(s3_contar videos-hls "$VIDEO_ID/")" = "0" ]; }
aguardar 30 sem_hls || true
echo "videos-hls/$VIDEO_ID/: $(s3_contar videos-hls "$VIDEO_ID/") objetos"

passo "Cota no identity_db (a reserva precisa ficar RELEASED)"
liberada() { [ "$(sql_db_valor identity_db "SELECT status FROM quota_reservations WHERE video_id = '$VIDEO_ID'")" = "RELEASED" ]; }
aguardar 30 liberada || true
sql_db identity_db "SELECT status, DATE_FORMAT(updated_at, '%T') AS hora FROM quota_reservations WHERE video_id = '$VIDEO_ID'"
RESERVA=$(sql_db_valor identity_db "SELECT status FROM quota_reservations WHERE video_id = '$VIDEO_ID'")

passo "Cliente notificado (upload_db.user_notification)"
sql_db upload_db "SELECT type, DATE_FORMAT(created_at, '%T') AS hora, message FROM user_notification WHERE video_id = '$VIDEO_ID' ORDER BY id"
NOTIFICADO=$(sql_db_valor upload_db "SELECT COUNT(*) FROM user_notification WHERE video_id = '$VIDEO_ID' AND type = 'VIDEO_FAILED'")

STATUS=$(sql_db_valor upload_db "SELECT CONCAT(status, '/', IFNULL(failure_reason, '')) FROM video_upload WHERE video_id = '$VIDEO_ID'")
PUBLICADOS=$(sql_db_valor upload_db "SELECT COUNT(*) FROM outbox WHERE message_key = '$VIDEO_ID' AND type = 'VideoPublished'")
CATALOGO=$(sql_db_valor download_db "SELECT COUNT(*) FROM video_catalog WHERE video_id = '$VIDEO_ID'")

if [ "$STATUS" = "FAILED/DEADLINE_EXPIRED" ] && tardio && [ "$PUBLICADOS" = "0" ] && [ "$CATALOGO" = "0" ] && sem_hls && [ "$RESERVA" = "RELEASED" ] && [ "$NOTIFICADO" -ge 1 ]; then
  resultado ok "prazo vencido compensado (cota devolvida, cliente notificado); resultado tardio descartado e HLS apagado; nada no catálogo."
else
  resultado falhou "status=$STATUS videoPublished=$PUBLICADOS catalogo=$CATALOGO reserva=$RESERVA notificado=$NOTIFICADO"
fi
