#!/bin/bash
# F2: uma réplica morre (docker kill) com mensagens em processamento; outra réplica assume e o efeito é único.
source "$(dirname "$0")/comum.sh"
exigir kafka mysql download

QTD=6
LOTE="falha-f2-$(date +%s)"
VITIMA=$(replicas | tail -1)

if [ "$(replicas | wc -l)" -lt 2 ]; then
  echo "O F2 precisa de pelo menos 2 réplicas do Download (DownloadReplicas >= 2)."
  exit 1
fi

titulo "F2 — Réplica morre no meio do processamento"
echo "Esperado: os $QTD eventos aplicados uma única vez, pelas réplicas que sobreviveram."
echo "Réplicas: $(for r in $(replicas); do printf '%s ' "$(nome_curto "$r")"; done)"

passo "docker kill $(nome_curto "$VITIMA") às $(date +%T)"
docker kill "$VITIMA" > /dev/null
echo "As partições de $(nome_curto "$VITIMA") ficam sem consumidor até o Kafka perceber a queda (~10 s)."

passo "Publicando $QTD eventos com $(nome_curto "$VITIMA") morto"
for i in $(seq 1 "$QTD"); do mensagem "$LOTE-$i" "$(uuid)" "F2 $i"; done | publicar

todos() {
  [ "$(sql_valor "SELECT COUNT(*) FROM video_catalog WHERE video_id LIKE '$LOTE-%'")" = "$QTD" ]
}
T0=$(date +%s)
aguardar 90 todos || true
echo "tempo até os $QTD eventos estarem no catálogo: $(( $(date +%s) - T0 ))s"

passo "Quem aplicou cada evento"
logs_replicas "aplicado: videoId=$LOTE"

passo "Catálogo"
sql "SELECT video_id, COUNT(*) AS linhas FROM video_catalog WHERE video_id LIKE '$LOTE-%' GROUP BY video_id ORDER BY video_id"

TOTAL=$(sql_valor "SELECT COUNT(*) FROM video_catalog WHERE video_id LIKE '$LOTE-%'")
DISTINTOS=$(sql_valor "SELECT COUNT(DISTINCT video_id) FROM video_catalog WHERE video_id LIKE '$LOTE-%'")

passo "Religando $(nome_curto "$VITIMA")"
docker start "$VITIMA" > /dev/null
echo "$(nome_curto "$VITIMA") religado; ele volta sozinho ao grupo do Kafka e ao gateway."

if [ "$TOTAL" = "$QTD" ] && [ "$DISTINTOS" = "$QTD" ]; then
  resultado ok "$QTD eventos aplicados uma única vez mesmo com $(nome_curto "$VITIMA") morto."
else
  resultado falhou "esperados=$QTD aplicados=$TOTAL distintos=$DISTINTOS"
fi
