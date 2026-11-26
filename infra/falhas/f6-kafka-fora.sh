#!/bin/bash
# F6: o Kafka fica fora do ar por 30 s enquanto um vídeo é enviado. Os eventos esperam no outbox,
# os serviços reconectam sozinhos, o outbox drena e nenhuma mensagem se perde.
source "$(dirname "$0")/upload-comum.sh"
exigir kafka mysql ministack download

PARADA=${PARADA_SEGUNDOS:-30}
KAFKA=$(container kafka)

# Todos os consumidores do Kafka que rodam em container: Download, Upload e media-processor.
consumidores() {
  docker ps --format "$SERVICO" | awk -F'\t' '
    $2 ~ /^(download|upload|media-processor)-[0-9]+$/ { print $1; next }
    $2 == "" && $1 ~ /^(download|upload|media-processor)-[0-9]+-/ { print $1 }' | sort || true
}

pendentes() {
  sql_db_valor "$1" "SELECT COUNT(*) FROM outbox WHERE published_at IS NULL"
}

titulo "F6 — Broker fora do ar por ${PARADA}s, com um vídeo sendo enviado"
echo "Esperado: o envio é aceito mesmo sem o Kafka; os eventos ficam no outbox; quando o broker volta,"
echo "todos os serviços reconectam sozinhos, o outbox drena e o vídeo chega publicado ao Download."

passo "Antes"
ANTES=$(consumidores)
REINICIOS_ANTES=0
for r in $ANTES; do
  n=$(docker inspect "$r" --format '{{.RestartCount}}')
  REINICIOS_ANTES=$((REINICIOS_ANTES + n))
  echo "  $(nome_curto "$r"): reinícios=$n"
done
echo "  outbox pendente: upload_db=$(pendentes upload_db) identity_db=$(pendentes identity_db) media_db=$(pendentes media_db)"

passo "Parando o Kafka às $(date +%T) ($(date -u +%T) UTC)"
docker stop "$KAFKA" > /dev/null

passo "Enviando um vídeo com o Kafka fora (em segundo plano)"
"$RAIZ/infra/upload/enviar-video.sh" --gerar 8 "F6 outbox" > "$SAIDA" 2>&1 &
ENVIO=$!

for t in $(seq 10 10 "$PARADA"); do
  sleep 10
  echo "  +${t}s  catálogo pelo gateway: $(status_http "$GATEWAY/api/download/videos")  outbox pendente no upload_db: $(pendentes upload_db)"
done
VIDEO_ID=$(sed -n 's/^Upload criado: videoId=\([^ ]*\).*/\1/p' "$SAIDA" | head -1)
echo "  envio aceito com o broker fora: videoId=${VIDEO_ID:-?}"

passo "Religando o Kafka às $(date +%T) ($(date -u +%T) UTC)"
docker start "$KAFKA" > /dev/null
broker_no_ar() { docker exec "$KAFKA" kafka-topics --bootstrap-server localhost:9092 --list; }
aguardar 60 broker_no_ar || true
T0=$(date +%s)

passo "Outbox drenando"
drenado() { [ "$(pendentes upload_db)" = "0" ] && [ "$(pendentes identity_db)" = "0" ] && [ "$(pendentes media_db)" = "0" ]; }
aguardar 120 drenado || true
echo "  outbox pendente: upload_db=$(pendentes upload_db) identity_db=$(pendentes identity_db) media_db=$(pendentes media_db) ($(( $(date +%s) - T0 ))s depois da volta)"

passo "O vídeo enviado durante a queda"
wait "$ENVIO" || true
grep -E "cota=|Publicado|Falhou" "$SAIDA" || true
publicado() { [ "$(sql_db_valor download_db "SELECT COUNT(*) FROM video_catalog WHERE video_id = '$VIDEO_ID'")" = "1" ]; }
aguardar 60 publicado || true
sql_db upload_db "SELECT type, DATE_FORMAT(created_at, '%T') AS criado_utc, DATE_FORMAT(published_at, '%T') AS publicado_utc FROM outbox WHERE message_key = '$VIDEO_ID' ORDER BY id"
STATUS=$(sql_db_valor upload_db "SELECT status FROM video_upload WHERE video_id = '$VIDEO_ID'")
CATALOGO=$(sql_db_valor download_db "SELECT COUNT(*) FROM video_catalog WHERE video_id = '$VIDEO_ID'")
echo "  status no Upload: $STATUS, no catálogo do Download: $CATALOGO linha"

passo "Depois"
REINICIOS=0
PARADAS=0
for r in $ANTES; do
  estado=$(docker inspect "$r" --format '{{.State.Status}}' 2> /dev/null || echo removido)
  n=$(docker inspect "$r" --format '{{.RestartCount}}' 2> /dev/null || echo 0)
  REINICIOS=$((REINICIOS + n))
  [ "$estado" = "running" ] || PARADAS=$((PARADAS + 1))
  echo "  $(nome_curto "$r"): estado=$estado reinícios=$n"
done
REINICIOS=$((REINICIOS - REINICIOS_ANTES))

if [ "$STATUS" = "PUBLISHED" ] && [ "$CATALOGO" = "1" ] && drenado && [ "$REINICIOS" -eq 0 ] && [ "$PARADAS" -eq 0 ]; then
  resultado ok "todos reconectaram sozinhos, o outbox drenou e o vídeo enviado durante a queda foi publicado."
else
  resultado falhou "status=$STATUS catalogo=$CATALOGO reinicios=$REINICIOS paradas=$PARADAS outbox_drenado=$(drenado && echo sim || echo nao)"
fi
