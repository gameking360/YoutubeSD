#!/bin/bash
# F4: mensagem fora do contrato vai para a DLQ e as mensagens seguintes continuam sendo processadas.
source "$(dirname "$0")/comum.sh"
exigir kafka mysql download

VIDEO="falha-f4-$(date +%s)"
DLQ="$TOPICO.dlq"

titulo "F4 — Mensagem venenosa (payload inválido)"
echo "Esperado: a inválida é tentada 4 vezes (1 + 3 novas tentativas, a cada 1 s), vai para $DLQ com o motivo,"
echo "e a válida publicada logo depois, na mesma partição, é processada normalmente."

passo "Publicando uma mensagem inválida e, em seguida, uma válida (mesma chave e partição)"
{
  echo "$VIDEO|{ isto nao e um JSON valido"
  mensagem "$VIDEO" "$(uuid)" "Depois da venenosa"
} | publicar

processada() {
  [ "$(sql_valor "SELECT COUNT(*) FROM video_catalog WHERE video_id = '$VIDEO'")" = "1" ]
}
aguardar 30 processada || true

passo "Mensagem na DLQ ($DLQ)"
NA_DLQ=$(docker exec "$(container kafka)" kafka-console-consumer \
  --bootstrap-server localhost:9092 --topic "$DLQ" --from-beginning --timeout-ms 8000 \
  --property print.key=true --property print.headers=true 2> /dev/null \
  | grep -a "$(printf '\t')$VIDEO$(printf '\t')" | tr -cd '[:print:]\t' || true)
if [ -n "$NA_DLQ" ]; then
  echo "chave:            $VIDEO"
  echo "$NA_DLQ" | grep -aoE "kafka_dlt-original-(topic|consumer-group):[^,	]*" | sed 's/^kafka_dlt-/  /'
  echo "conteúdo original: $(echo "$NA_DLQ" | awk -F'\t' '{print $NF}')"
else
  echo "nenhuma mensagem com a chave $VIDEO na DLQ"
fi

passo "Tentativas, DLQ e a mensagem seguinte (logs das réplicas)"
logs_replicas "$VIDEO"
TENTATIVAS=$(logs_replicas "Tentativa .* chave=$VIDEO" | wc -l)
echo "tentativas que falharam antes da DLQ: $TENTATIVAS"

passo "A mensagem válida seguinte"
sql "SELECT video_id, title, status FROM video_catalog WHERE video_id = '$VIDEO'"

if [ -n "$NA_DLQ" ] && processada && [ "$TENTATIVAS" -ge 3 ]; then
  resultado ok "a inválida foi tentada $TENTATIVAS vezes, foi para a DLQ, e a válida seguinte foi processada."
else
  resultado falhou "na_dlq=$([ -n "$NA_DLQ" ] && echo sim || echo nao) valida_processada=$(processada && echo sim || echo nao) tentativas=$TENTATIVAS"
fi
