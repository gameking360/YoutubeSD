#!/bin/bash
# F2: docker kill numa réplica do Download enquanto ela aplica um lote de eventos.
# O que ela não confirmou é reentregue a outra réplica, e cada evento é aplicado uma única vez.
source "$(dirname "$0")/comum.sh"
exigir kafka mysql download

QTD=${QTD:-600}
LOTE="falha-f2-$(date +%s)"
REPLICAS=$(replicas)
VITIMA=$(echo "$REPLICAS" | tail -1)

if [ "$(echo "$REPLICAS" | wc -l)" -lt 2 ]; then
  echo "O F2 precisa de pelo menos 2 réplicas do Download (DownloadReplicas >= 2)."
  exit 1
fi

titulo "F2 — docker kill no consumidor durante o processamento"
echo "Esperado: $(nome_curto "$VITIMA") morre no meio do lote; as partições dele passam para outra réplica,"
echo "o que ele não confirmou é reentregue, e cada um dos $QTD eventos é aplicado uma única vez."
echo "Réplicas: $(for r in $REPLICAS; do printf '%s ' "$(nome_curto "$r")"; done)"

# Conta nos logs de uma réplica (viva ou morta) os eventos do lote que ela aplicou ou ignorou como repetidos.
aplicados_por() { docker logs --since "$INICIO" "$1" 2>&1 | grep -c "aplicado: videoId=$LOTE-" || true; }
repetidos_por() { docker logs --since "$INICIO" "$1" 2>&1 | grep -c "duplicada ignorada.*correlationId=$LOTE-" || true; }

passo "Divisão das partições antes"
particoes

passo "Publicando $QTD eventos com chaves diferentes (espalhados pelas 3 partições)"
for i in $(seq 1 "$QTD"); do mensagem "$LOTE-$i" "$(uuid_seq "$i")" "F2 $i"; done | publicar &
PUBLICADOR=$!

for _ in $(seq 1 300); do [ "$(aplicados_por "$VITIMA")" -ge 5 ] && break; sleep 0.2; done
NA_HORA=$(aplicados_por "$VITIMA")
trap 'docker start "$VITIMA" > /dev/null 2>&1 || true' EXIT
docker kill "$VITIMA" > /dev/null
T0=$(date +%s)
passo "docker kill $(nome_curto "$VITIMA") às $(date +%T) — ele já tinha aplicado $NA_HORA eventos do lote"
echo "As partições dele ficam sem dono até o Kafka notar a falta de heartbeat (session.timeout.ms = 10 s)."
wait "$PUBLICADOR" || true

todos() {
  [ "$(sql_valor "SELECT COUNT(*) FROM video_catalog WHERE video_id LIKE '$LOTE-%'")" = "$QTD" ]
}
aguardar 120 todos || true
echo "tempo da queda até os $QTD eventos estarem no catálogo: $(( $(date +%s) - T0 ))s"

passo "Divisão das partições depois da queda"
particoes

passo "Quem aplicou o lote (logs de todas as réplicas, inclusive a morta)"
APLICADOS=0
REPETIDOS=0
for r in $REPLICAS; do
  a=$(aplicados_por "$r")
  d=$(repetidos_por "$r")
  APLICADOS=$((APLICADOS + a))
  REPETIDOS=$((REPETIDOS + d))
  echo "  $(nome_curto "$r"): aplicou $a, ignorou $d como repetidos"
done
echo "  total de efeitos aplicados: $APLICADOS (precisa ser exatamente $QTD)"

passo "Estado no banco (download_db)"
CATALOGO=$(sql_valor "SELECT COUNT(*) FROM video_catalog WHERE video_id LIKE '$LOTE-%'")
REGISTROS=$(sql_valor "SELECT COUNT(*) FROM processed_messages WHERE type = 'VideoPublished' AND processed_at >= '$(date -u -d "$INICIO" '+%Y-%m-%d %H:%M:%S')'")
echo "  linhas no catálogo: $CATALOGO"
echo "  messageIds registrados em processed_messages: $REGISTROS"

passo "Religando $(nome_curto "$VITIMA") (após o reinício ele volta ao grupo)"
docker start "$VITIMA" > /dev/null
saudavel() { [ "$(porta_health "$VITIMA")" = "200" ]; }
aguardar 90 saudavel || true
tres() { [ "$(membros_grupo)" -ge "$(echo "$REPLICAS" | wc -l)" ]; }
aguardar 60 tres || true
echo "$(nome_curto "$VITIMA"): /health $(porta_health "$VITIMA"), réplicas no grupo: $(membros_grupo)"
particoes

if [ "$CATALOGO" = "$QTD" ] && [ "$APLICADOS" = "$QTD" ] && [ "$REGISTROS" = "$QTD" ] && [ "$NA_HORA" -lt "$QTD" ]; then
  resultado ok "$(nome_curto "$VITIMA") morreu no meio do lote; os $QTD eventos foram aplicados uma única vez ($REPETIDOS reentregas ignoradas)."
else
  resultado falhou "catalogo=$CATALOGO aplicados=$APLICADOS registros=$REGISTROS esperados=$QTD"
fi
