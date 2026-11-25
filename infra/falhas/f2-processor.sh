#!/bin/bash
# F2 no media-processor: docker kill na réplica que está processando → outra réplica assume, sem efeito duplicado.
source "$(dirname "$0")/upload-comum.sh"
exigir kafka mysql ministack
[ -n "$(processadores)" ] || { echo "Nenhum media-processor rodando. Suba o AppHost ou o Docker Compose antes."; exit 1; }
trap caos_reset EXIT

if [ "$(processadores | wc -l)" -lt 2 ]; then
  echo "O F2 precisa de pelo menos 2 réplicas do media-processor (ProcessorReplicas >= 2)."
  exit 1
fi

titulo "F2 — media-processor morre no meio do processamento"
echo "Esperado: o Kafka passa a partição para outra réplica (~10 s), ela processa de novo (tentativa 2)"
echo "e o vídeo é publicado uma única vez."
echo "Réplicas: $(for r in $(processadores); do printf '%s ' "$(nome_curto "$r")"; done)"

passo "Deixando cada tentativa mais lenta (20 s) para dar tempo de derrubar a réplica"
caos 20000 NULL

passo "Enviando um vídeo de 10 s em segundo plano"
"$RAIZ/infra/upload/enviar-video.sh" --gerar 10 "F2 processor" > "$SAIDA" 2>&1 &
ENVIO=$!

criado() { grep -q "Upload criado: videoId=" "$SAIDA"; }
aguardar 120 criado
VIDEO_ID=$(sed -n 's/.*Upload criado: videoId=\([^ ]*\).*/\1/p' "$SAIDA")
echo "videoId=$VIDEO_ID"

rodando() { [ -n "$(sql_db_valor media_db "SELECT instance_id FROM processing_job WHERE video_id = '$VIDEO_ID' AND status = 'RUNNING'")" ]; }
aguardar 120 rodando
VITIMA_NOME=$(sql_db_valor media_db "SELECT instance_id FROM processing_job WHERE video_id = '$VIDEO_ID'")
VITIMA=$(for r in $(processadores); do [ "$(nome_curto "$r")" = "$VITIMA_NOME" ] && echo "$r"; done | head -1)
sleep 3

passo "docker kill $VITIMA_NOME às $(date +%T) (processando o vídeo)"
docker kill "$VITIMA" > /dev/null

passo "Aguardando outra réplica concluir"
wait "$ENVIO" || true
cat "$SAIDA" | grep -E "PROCESSING|PUBLISHED|Falhou" || true

passo "Quem processou (media_db.processing_job e histórico da saga)"
sql_db media_db "SELECT attempts, status, instance_id, DATE_FORMAT(finished_at, '%T') AS fim FROM processing_job WHERE video_id = '$VIDEO_ID'"
historico
logs_de media-processor "videoId=$VIDEO_ID" | grep -E "iniciado|processado|interrompido|descartado" || true

PUBLICADOS=$(sql_db_valor upload_db "SELECT COUNT(*) FROM outbox WHERE message_key = '$VIDEO_ID' AND type = 'VideoPublished'")
CATALOGO=$(sql_db_valor download_db "SELECT COUNT(*) FROM video_catalog WHERE video_id = '$VIDEO_ID'")
STATUS=$(sql_db_valor upload_db "SELECT status FROM video_upload WHERE video_id = '$VIDEO_ID'")
QUEM=$(sql_db_valor media_db "SELECT instance_id FROM processing_job WHERE video_id = '$VIDEO_ID'")

passo "Religando $VITIMA_NOME"
docker start "$VITIMA" > /dev/null
echo "$VITIMA_NOME religado; volta sozinho ao grupo media-processor."

if [ "$STATUS" = "PUBLISHED" ] && [ "$PUBLICADOS" = "1" ] && [ "$CATALOGO" = "1" ] && [ "$QUEM" != "$VITIMA_NOME" ]; then
  resultado ok "$QUEM assumiu depois da queda de $VITIMA_NOME; publicado uma única vez."
else
  resultado falhou "status=$STATUS videoPublished=$PUBLICADOS catalogo=$CATALOGO concluido_por=$QUEM"
fi
