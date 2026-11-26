#!/bin/bash
# F6: o Kafka fica fora do ar; o Download continua de pé e volta a consumir sozinho quando o broker retorna.
source "$(dirname "$0")/comum.sh"
exigir kafka mysql download

PARADA=${PARADA_SEGUNDOS:-30}
VIDEO="falha-f6-$(date +%s)"
KAFKA=$(container kafka)

titulo "F6 — Broker fora do ar por ${PARADA}s"
echo "Esperado: o Download segue respondendo; ao voltar, o Kafka entrega o evento novo sem reiniciar nenhuma réplica."

passo "Antes"
ANTES=$(replicas)
REINICIOS_ANTES=0
for r in $ANTES; do
  n=$(docker inspect "$r" --format '{{.RestartCount}}')
  REINICIOS_ANTES=$((REINICIOS_ANTES + n))
  echo "$(nome_curto "$r"): reinícios=$n"
done

passo "Parando o Kafka às $(date +%T)"
docker stop "$KAFKA" > /dev/null
for t in $(seq 10 10 "$PARADA"); do
  sleep 10
  echo "  +${t}s  catálogo pelo gateway: $(status_http "$GATEWAY/api/download/videos")"
done

passo "Religando o Kafka às $(date +%T)"
docker start "$KAFKA" > /dev/null
broker_no_ar() {
  docker exec "$KAFKA" kafka-topics --bootstrap-server localhost:9092 --list
}
aguardar 60 broker_no_ar || true

passo "Publicando um evento novo"
mensagem "$VIDEO" "$(uuid)" "Depois da queda do Kafka" | publicar
processado() {
  [ "$(sql_valor "SELECT COUNT(*) FROM video_catalog WHERE video_id = '$VIDEO'")" = "1" ]
}
T0=$(date +%s)
aguardar 60 processado || true
echo "tempo até o evento estar no catálogo: $(( $(date +%s) - T0 ))s"
sql "SELECT video_id, title, status FROM video_catalog WHERE video_id = '$VIDEO'"

passo "Depois"
REINICIOS=0
PARADAS=0
for r in $ANTES; do
  estado=$(docker inspect "$r" --format '{{.State.Status}}' 2> /dev/null || echo removido)
  n=$(docker inspect "$r" --format '{{.RestartCount}}' 2> /dev/null || echo 0)
  REINICIOS=$((REINICIOS + n))
  [ "$estado" = "running" ] || PARADAS=$((PARADAS + 1))
  echo "$(nome_curto "$r"): estado=$estado reinícios=$n"
done
REINICIOS=$((REINICIOS - REINICIOS_ANTES))

if processado && [ "$REINICIOS" -eq 0 ] && [ "$PARADAS" -eq 0 ]; then
  resultado ok "o Download continuou no ar e consumiu o evento novo sem reiniciar."
else
  resultado falhou "processado=$(processado && echo sim || echo nao) reinicios=$REINICIOS paradas=$PARADAS"
fi
