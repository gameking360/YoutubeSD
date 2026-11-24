#!/bin/bash
# Funções do roteiro de falhas do Upload/media-processor. Não execute diretamente.
source "$(dirname "${BASH_SOURCE[0]}")/comum.sh"

RAIZ="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SAIDA=$(mktemp)

# sql_db <database> <sql> / sql_db_valor <database> <sql>
sql_db() {
  docker exec "$(container mysql)" mysql --default-character-set=utf8mb4 -uroot -proot123 "$1" -e "$2" 2> /dev/null
}

sql_db_valor() {
  docker exec "$(container mysql)" mysql --default-character-set=utf8mb4 -uroot -proot123 "$1" -N -e "$2" 2> /dev/null
}

# caos <latency_ms|NULL> <failure_rate|NULL>: vale para todas as réplicas do media-processor, na hora.
caos() {
  sql_db media_db "UPDATE chaos_settings SET latency_ms = $1, failure_rate = $2, updated_at = UTC_TIMESTAMP(3) WHERE id = 1"
}

caos_reset() {
  caos NULL NULL || true
  rm -f "$SAIDA"
}

processadores() {
  docker ps --format "$SERVICO" | awk -F'\t' '
    $2 ~ /^media-processor-[0-9]+$/ { print $1; next }
    $2 == "" && $1 ~ /^media-processor-[0-9]+-/ { print $1 }' | sort || true
}

# s3_contar <bucket> <prefixo>: quantos objetos existem no prefixo
s3_contar() {
  docker exec -e AWS_ACCESS_KEY_ID=test -e AWS_SECRET_ACCESS_KEY=test -e AWS_DEFAULT_REGION=us-east-1 "$(container ministack)" \
    aws --endpoint-url http://localhost:4566 s3 ls "s3://$1/$2" --recursive 2> /dev/null | wc -l | tr -d ' '
}

# enviar <segundos> <titulo>: gera um vídeo de teste, envia pelo gateway e define VIDEO_ID
enviar() {
  "$RAIZ/infra/upload/enviar-video.sh" --gerar "$1" "$2" | tee "$SAIDA" || true
  VIDEO_ID=$(sed -n 's/^videoId=//p' "$SAIDA" | tail -1)
  [ -n "$VIDEO_ID" ] || { echo "O envio não devolveu um videoId."; exit 1; }
}

# logs_de <prefixo-do-serviço> <padrão>: linhas dos logs desde o início do script, com o nome da réplica
logs_de() {
  docker ps --format "$SERVICO" | awk -F'\t' -v s="$1" '
    $2 ~ ("^" s "-[0-9]+$") { print $1; next }
    $2 == "" && index($1, s "-") == 1 { print $1 }' | sort | while read -r r; do
    docker logs --since "$INICIO" "$r" 2>&1 | grep -E "$2" | sed -E "s/^ +//; s/^/[$(nome_curto "$r")] /" || true
  done
}

estado_video() {
  sql_db upload_db "SELECT status, failure_reason, quota_state, upload_state, processing_attempts FROM video_upload WHERE video_id = '$VIDEO_ID'"
}

historico() {
  sql_db upload_db "SELECT DATE_FORMAT(at, '%T') AS hora, from_status, to_status, reason, instance_id, LEFT(detail, 70) AS detalhe FROM video_history WHERE video_id = '$VIDEO_ID' ORDER BY id"
}
