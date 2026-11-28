#!/bin/bash
# Versionamento de eventos: VideoPublished v1 e v2 convivem no mesmo tópico.
# O Download aceita as duas; o Identity, que só lê o videoId, continua funcionando;
# uma versão desconhecida (v3) vai para a DLQ do Download.
source "$(dirname "$0")/../falhas/comum.sh"
exigir kafka mysql download

BASE="falha-versao-$(date +%s)"
V1="$BASE-v1"
V2="$BASE-v2"
V3="$BASE-v3"
ID1=$(uuid_seq 1)
ID2=$(uuid_seq 2)
ID3=$(uuid_seq 3)
DLQ="$TOPICO.dlq"

# mensagem_v2 <videoId> <messageId> <titulo> [versao]: imprime "chave|json" no formato da v2
mensagem_v2() {
  local quando
  quando=$(agora)
  printf '%s|{"messageId":"%s","correlationId":"%s","type":"VideoPublished","version":%s,"occurredAt":"%s","payload":{"videoId":"%s","ownerId":"roteiro","title":"%s","visibility":"UNLISTED","durationMs":20000,"hls":{"bucket":"videos-hls","masterKey":"demo-1/master.m3u8","renditions":[{"name":"360p","width":640,"height":360,"bandwidth":928000}]},"publishedAt":"%s"}}\n' \
    "$1" "$2" "$1" "${4:-2}" "$quando" "$1" "$3" "$quando"
}

titulo "Versionamento — VideoPublished v1 e v2 no mesmo tópico"
echo "Esperado: o Download aplica a v1 e a v2 (a v2 tem o HLS agrupado em \"hls\" e a duração em ms);"
echo "o Identity, que só conhece o videoId, processa as duas sem erro; a v3 vai para a DLQ do Download."

passo "Publicando uma v1, uma v2 e uma v3 em $TOPICO"
{
  mensagem "$V1" "$ID1" "Publicado na v1"
  mensagem_v2 "$V2" "$ID2" "Publicado na v2"
  mensagem_v2 "$V3" "$ID3" "Publicado na v3" 3
} | publicar

aplicadas() {
  [ "$(sql_valor "SELECT COUNT(*) FROM video_catalog WHERE video_id IN ('$V1', '$V2')")" = "2" ]
}
aguardar 30 aplicadas || true

passo "Catálogo do Download (download_db.video_catalog)"
sql "SELECT video_id, title, event_version, duration_sec, master_key FROM video_catalog WHERE video_id LIKE '$BASE-%' ORDER BY video_id"

passo "Logs do Download"
logs_replicas "$BASE"

passo "A v3 na DLQ ($DLQ)"
NA_DLQ=$(docker exec "$(container kafka)" kafka-console-consumer \
  --bootstrap-server localhost:9092 --topic "$DLQ" --from-beginning --timeout-ms 8000 \
  --property print.key=true --property print.headers=true 2> /dev/null \
  | grep -a "$(printf '\t')$V3$(printf '\t')" | tr -cd '[:print:]\t' || true)
if [ -n "$NA_DLQ" ]; then
  echo "chave: $V3"
  echo "$NA_DLQ" | grep -aoE "kafka_dlt-original-(topic|consumer-group):[^,	]*" | sed 's/^kafka_dlt-/  /' || true
else
  echo "nenhuma mensagem com a chave $V3 na DLQ"
fi
MOTIVO=$(logs_replicas "Mensagem enviada para $DLQ: chave=$V3" | sed -n 's/.*motivo=//p' | head -1)
echo "motivo (log do Download): ${MOTIVO:-não encontrado}"

passo "Identity, o consumidor antigo (identity_db.processed_messages)"
processadas_identity() {
  [ "$(docker exec "$(container mysql)" mysql -uroot -proot123 identity_db -N -e \
    "SELECT COUNT(*) FROM processed_messages WHERE message_id IN ('$ID1', '$ID2')" 2> /dev/null)" = "2" ]
}
aguardar 30 processadas_identity || true
docker exec "$(container mysql)" mysql -uroot -proot123 identity_db -e \
  "SELECT message_id, consumer, topic FROM processed_messages WHERE message_id IN ('$ID1', '$ID2', '$ID3') ORDER BY message_id" 2> /dev/null

VERSAO1=$(sql_valor "SELECT event_version FROM video_catalog WHERE video_id = '$V1'")
VERSAO2=$(sql_valor "SELECT event_version FROM video_catalog WHERE video_id = '$V2'")
DURACAO2=$(sql_valor "SELECT duration_sec FROM video_catalog WHERE video_id = '$V2'")
V3_NO_CATALOGO=$(sql_valor "SELECT COUNT(*) FROM video_catalog WHERE video_id = '$V3'")

if [ "$VERSAO1" = "1" ] && [ "$VERSAO2" = "2" ] && [ "$DURACAO2" = "20" ] && [ "$V3_NO_CATALOGO" = "0" ] \
  && [ -n "$NA_DLQ" ] && echo "$MOTIVO" | grep -q "v3 não é suportado" && processadas_identity; then
  resultado ok "v1 e v2 aplicadas pelo Download, processadas pelo Identity sem erro, e a v3 na DLQ."
else
  resultado falhou "v1=$VERSAO1 v2=$VERSAO2 duracao_v2=$DURACAO2 v3_no_catalogo=$V3_NO_CATALOGO motivo_dlq=${MOTIVO:-nenhum}"
fi
