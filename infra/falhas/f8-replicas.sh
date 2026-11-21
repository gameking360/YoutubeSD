#!/bin/bash
# F8: com 3 réplicas, as partições e a carga são divididas entre elas, sem efeito duplicado.
source "$(dirname "$0")/comum.sh"
exigir kafka mysql download

QTD=12
LOTE="falha-f8-$(date +%s)"
KAFKA=$(container kafka)

titulo "F8 — Escala horizontal com $(replicas | wc -l) réplicas"
echo "Esperado: partições divididas entre as réplicas, eventos processados por mais de uma réplica, nenhum duplicado."

passo "Divisão das partições de $TOPICO no grupo download"
declare -A IP_REPLICA
for r in $(replicas); do
  ip=$(docker inspect "$r" --format '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}')
  IP_REPLICA[$ip]=$(nome_curto "$r")
done
docker exec "$KAFKA" kafka-consumer-groups --bootstrap-server localhost:9092 --describe --group download 2> /dev/null \
  | awk -v t="$TOPICO" '$2 == t {print $3, $8}' | sort -n | while read -r particao host; do
    echo "  partição $particao -> ${IP_REPLICA[${host#/}]:-$host}"
  done

passo "Publicando $QTD eventos com chaves diferentes"
for i in $(seq 1 "$QTD"); do mensagem "$LOTE-$i" "$(uuid)" "F8 $i"; done | publicar
todos() {
  [ "$(sql_valor "SELECT COUNT(*) FROM video_catalog WHERE video_id LIKE '$LOTE-%'")" = "$QTD" ]
}
aguardar 60 todos || true

passo "Quantos eventos cada réplica aplicou"
USADAS=0
for r in $(replicas); do
  n=$(docker logs --since "$INICIO" "$r" 2>&1 | grep -c "aplicado: videoId=$LOTE" || true)
  [ "$n" -gt 0 ] && USADAS=$((USADAS + 1))
  echo "  $(nome_curto "$r"): $n"
done

passo "Round Robin do gateway (9 requisições)"
for _ in $(seq 1 9); do
  printf '%s ' "$(instancia_http "$GATEWAY/api/download/videos")"
done
echo

TOTAL=$(sql_valor "SELECT COUNT(*) FROM video_catalog WHERE video_id LIKE '$LOTE-%'")
DISTINTOS=$(sql_valor "SELECT COUNT(DISTINCT video_id) FROM video_catalog WHERE video_id LIKE '$LOTE-%'")
echo
echo "eventos aplicados: $TOTAL de $QTD (distintos: $DISTINTOS), réplicas que processaram: $USADAS"

if [ "$TOTAL" = "$QTD" ] && [ "$DISTINTOS" = "$QTD" ] && [ "$USADAS" -ge 2 ]; then
  resultado ok "carga dividida entre $USADAS réplicas, sem duplicar."
else
  resultado falhou "aplicados=$TOTAL distintos=$DISTINTOS replicas_usadas=$USADAS"
fi
