#!/bin/bash
# Remove do catálogo os vídeos criados pelo roteiro de falhas (ids que começam com "falha-").
source "$(dirname "$0")/comum.sh"
exigir mysql

ANTES=$(sql_valor "SELECT COUNT(*) FROM video_catalog WHERE video_id LIKE 'falha-%'")
sql "DELETE FROM video_catalog WHERE video_id LIKE 'falha-%'"
echo "Removidos $ANTES vídeos de teste do catálogo."
