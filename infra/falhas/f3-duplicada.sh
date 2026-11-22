#!/bin/bash
# F3: a mesma mensagem (mesmo messageId) publicada 5 vezes gera um único efeito.
source "$(dirname "$0")/comum.sh"
exigir kafka mysql download

VIDEO="falha-f3-$(date +%s)"
MESSAGE_ID=$(uuid)

titulo "F3 — Mensagem duplicada (mesmo messageId 5 vezes)"
echo "Esperado: 1 linha no catálogo, messageId registrado 1 vez, 4 duplicatas ignoradas no log."

passo "Antes"
echo "linhas no catálogo para $VIDEO: $(sql_valor "SELECT COUNT(*) FROM video_catalog WHERE video_id = '$VIDEO'")"
echo "registros do messageId $MESSAGE_ID: $(sql_valor "SELECT COUNT(*) FROM processed_messages WHERE message_id = '$MESSAGE_ID'")"

passo "Publicando a mesma mensagem 5 vezes"
LINHA=$(mensagem "$VIDEO" "$MESSAGE_ID" "Duplicada")
for _ in 1 2 3 4 5; do echo "$LINHA"; done | publicar

duplicatas() {
  [ "$(logs_replicas "duplicada ignorada.*$MESSAGE_ID" | wc -l)" -ge 4 ]
}
aguardar 30 duplicatas || true

passo "Depois"
sql "SELECT video_id, title, status FROM video_catalog WHERE video_id = '$VIDEO'"
sql "SELECT message_id, type, processed_at FROM processed_messages WHERE message_id = '$MESSAGE_ID'"

passo "Logs das réplicas"
logs_replicas "$MESSAGE_ID"

LINHAS=$(sql_valor "SELECT COUNT(*) FROM video_catalog WHERE video_id = '$VIDEO'")
REGISTROS=$(sql_valor "SELECT COUNT(*) FROM processed_messages WHERE message_id = '$MESSAGE_ID'")
IGNORADAS=$(logs_replicas "duplicada ignorada.*$MESSAGE_ID" | wc -l)

if [ "$LINHAS" = "1" ] && [ "$REGISTROS" = "1" ] && [ "$IGNORADAS" -eq 4 ]; then
  resultado ok "1 efeito, 1 registro do messageId e 4 duplicatas ignoradas."
else
  resultado falhou "linhas=$LINHAS registros=$REGISTROS duplicatas_ignoradas=$IGNORADAS"
fi
