#!/bin/bash
# F8: o consumidor sobe de 1 para 3 réplicas; o Kafka redistribui as partições e a carga é dividida, sem efeito duplicado.
source "$(dirname "$0")/comum.sh"
exigir kafka mysql download

REPLICAS=$(replicas)
PRIMEIRA=$(echo "$REPLICAS" | head -1)
OUTRAS=$(echo "$REPLICAS" | tail -n +2)
LOTE="falha-f8-$(date +%s)"

if [ "$(echo "$REPLICAS" | wc -l)" -lt 3 ]; then
  echo "O F8 precisa das 3 réplicas do Download no ar (DownloadReplicas >= 3)."
  exit 1
fi

titulo "F8 — Escala horizontal: subindo de 1 para 3 réplicas"
echo "Esperado: com 1 réplica, ela é dona das 3 partições; ao subir mais 2, o Kafka redistribui"
echo "uma partição para cada, e um lote novo é dividido entre as 3, cada evento aplicado uma vez."

aplicados_desde() { docker logs --since "$2" "$1" 2>&1 | grep -c "aplicado: videoId=$3" || true; }

passo "Deixando só $(nome_curto "$PRIMEIRA"): docker stop nas outras"
trap 'docker start $OUTRAS > /dev/null 2>&1 || true' EXIT
docker stop $OUTRAS > /dev/null
uma() { [ "$(membros_grupo)" = "1" ]; }
aguardar 60 uma || true
echo "réplicas no grupo download: $(membros_grupo)"
particoes

passo "Publicando 12 eventos com 1 réplica"
MARCA1=$(date -u +%Y-%m-%dT%H:%M:%SZ)
for i in $(seq 1 12); do mensagem "$LOTE-a$i" "$(uuid_seq "$i")" "F8 1 replica $i"; done | publicar
lote_a() { [ "$(sql_valor "SELECT COUNT(*) FROM video_catalog WHERE video_id LIKE '$LOTE-a%'")" = "12" ]; }
aguardar 60 lote_a || true
echo "  $(nome_curto "$PRIMEIRA"): $(aplicados_desde "$PRIMEIRA" "$MARCA1" "$LOTE-a") de 12"

passo "Subindo para 3 réplicas: docker start $(for r in $OUTRAS; do printf '%s ' "$(nome_curto "$r")"; done)"
docker start $OUTRAS > /dev/null
for r in $OUTRAS; do
  saudavel() { [ "$(porta_health "$r")" = "200" ]; }
  aguardar 90 saudavel || true
  echo "  $(nome_curto "$r"): /health $(porta_health "$r")"
done
tres() { [ "$(membros_grupo)" = "3" ]; }
aguardar 60 tres || true
echo "réplicas no grupo download: $(membros_grupo) (o Kafka fez o rebalanceamento)"
particoes

QTD=30
passo "Publicando $QTD eventos com 3 réplicas"
MARCA2=$(date -u +%Y-%m-%dT%H:%M:%SZ)
for i in $(seq 1 "$QTD"); do mensagem "$LOTE-b$i" "$(uuid_seq "$((100 + i))")" "F8 3 replicas $i"; done | publicar
lote_b() { [ "$(sql_valor "SELECT COUNT(*) FROM video_catalog WHERE video_id LIKE '$LOTE-b%'")" = "$QTD" ]; }
aguardar 60 lote_b || true

passo "Quantos eventos cada réplica aplicou"
APLICADOS=0
USADAS=0
for r in $REPLICAS; do
  n=$(aplicados_desde "$r" "$MARCA2" "$LOTE-b")
  APLICADOS=$((APLICADOS + n))
  [ "$n" -gt 0 ] && USADAS=$((USADAS + 1))
  echo "  $(nome_curto "$r"): $n"
done
CATALOGO=$(sql_valor "SELECT COUNT(*) FROM video_catalog WHERE video_id LIKE '$LOTE-b%'")
echo "efeitos aplicados: $APLICADOS, linhas no catálogo: $CATALOGO (os dois precisam ser $QTD)"

passo "Gateway: Round Robin entre as 3 réplicas (9 requisições)"
tres_no_gateway() {
  [ "$(for _ in 1 2 3 4 5 6; do instancia_http "$GATEWAY/api/download/videos"; done | sort -u | wc -l)" -ge 3 ]
}
aguardar 30 tres_no_gateway || true
for _ in $(seq 1 9); do printf '%s ' "$(instancia_http "$GATEWAY/api/download/videos")"; done
echo

if [ "$APLICADOS" = "$QTD" ] && [ "$CATALOGO" = "$QTD" ] && [ "$USADAS" -ge 2 ]; then
  resultado ok "de 1 para 3 réplicas: partições redistribuídas e $QTD eventos divididos entre $USADAS réplicas, sem duplicar."
else
  resultado falhou "aplicados=$APLICADOS catalogo=$CATALOGO replicas_usadas=$USADAS"
fi
