#!/bin/bash
# Funções compartilhadas pelo roteiro de falhas. Não execute diretamente.
set -euo pipefail
export MSYS_NO_PATHCONV=1

TOPICO="${KAFKA_TOPIC_VIDEO_PUBLISHED:-video.published}"
GATEWAY="${GATEWAY_URL:-http://localhost:8080}"
INICIO=$(date -u +%Y-%m-%dT%H:%M:%SZ)

# Os containers são encontrados tanto no Aspire (nome "kafka-<sufixo>")
# quanto no Docker Compose (etiqueta com.docker.compose.service=kafka).
SERVICO='{{.Names}}	{{.Label "com.docker.compose.service"}}'

container() {
  docker ps --format "$SERVICO" | awk -F'\t' -v s="$1" '
    $2 == s { print $1; exit }
    $2 == "" && index($1, s "-") == 1 && $1 !~ /-ui-/ { print $1; exit }' || true
}

replicas() {
  docker ps --format "$SERVICO" | awk -F'\t' '
    $2 ~ /^download-[0-9]+$/ { print $1; next }
    $2 == "" && $1 ~ /^download-[0-9]+-/ { print $1 }' | sort || true
}

nome_curto() {
  local servico
  servico=$(docker inspect "$1" --format '{{index .Config.Labels "com.docker.compose.service"}}' 2> /dev/null || true)
  if [ -n "$servico" ]; then
    echo "$servico"
  else
    echo "${1%-*}"
  fi
}

exigir() {
  for nome in "$@"; do
    if [ "$nome" = "download" ]; then
      encontrado=$(replicas | head -1)
    else
      encontrado=$(container "$nome")
    fi
    if [ -z "$encontrado" ]; then
      echo "O container '$nome' não está rodando. Suba o AppHost ou o Docker Compose antes."
      exit 1
    fi
  done
}

titulo() {
  echo
  echo "=================================================================="
  echo " $*"
  echo "=================================================================="
}

passo() {
  echo
  echo "--> $*"
}

resultado() {
  echo
  if [ "$1" = "ok" ]; then
    echo "RESULTADO: OK — $2"
  else
    echo "RESULTADO: FALHOU — $2"
    exit 1
  fi
}

uuid() {
  docker exec "$(container kafka)" cat /proc/sys/kernel/random/uuid
}

# uuid_seq <n>: messageId único para o n-ésimo evento de um lote (segundo atual + n), no formato UUID.
# Gerado no shell, sem docker exec, para lotes grandes. Dois lotes no mesmo segundo nunca rodam juntos.
UUID_BASE=$(date +%s)
uuid_seq() {
  printf '%08x-0000-4000-8000-%012x' "$UUID_BASE" "$1"
}

# porta_health <container>: código HTTP do /health da réplica, pela porta publicada no host
porta_health() {
  local porta
  porta=$(docker port "$1" 8080 2> /dev/null | head -1 | sed 's/.*://')
  [ -n "$porta" ] || { echo "000"; return; }
  status_http "http://localhost:$porta/health"
}

# membros_grupo: quantas réplicas estão no grupo download agora (cada réplica tem um consumidor por tópico)
membros_grupo() {
  docker exec "$(container kafka)" kafka-consumer-groups --bootstrap-server localhost:9092     --describe --group download --members 2> /dev/null | awk '$1 == "download" {print $3}' | sort -u | wc -l | tr -d ' '
}

# particoes: dono de cada partição do tópico no grupo download, com o nome da réplica
particoes() {
  declare -A ip_replica
  local r ip
  for r in $(replicas); do
    ip=$(docker inspect "$r" --format '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}')
    ip_replica[$ip]=$(nome_curto "$r")
  done
  docker exec "$(container kafka)" kafka-consumer-groups --bootstrap-server localhost:9092 --describe --group download 2> /dev/null     | awk -v t="$TOPICO" '$2 == t {print $3, $8}' | sort -n | while read -r particao host; do
      echo "  partição $particao -> ${ip_replica[${host#/}]:-sem dono}"
    done
}

agora() {
  date -u +%Y-%m-%dT%H:%M:%S.%3NZ
}

# mensagem <videoId> <messageId> <titulo>: imprime "chave|json" de um VideoPublished v1
mensagem() {
  local quando
  quando=$(agora)
  printf '%s|{"messageId":"%s","correlationId":"%s","type":"VideoPublished","version":1,"occurredAt":"%s","payload":{"videoId":"%s","ownerId":"roteiro","title":"%s","visibility":"UNLISTED","durationSec":20,"hlsBucket":"videos-hls","masterKey":"demo-1/master.m3u8","renditions":[{"name":"360p","width":640,"height":360,"bandwidth":928000}],"publishedAt":"%s"}}\n' \
    "$1" "$2" "$1" "$quando" "$1" "$3" "$quando"
}

# publicar [topico]: lê linhas "chave|valor" da entrada e publica no Kafka
publicar() {
  docker exec -i "$(container kafka)" kafka-console-producer \
    --bootstrap-server localhost:9092 --topic "${1:-$TOPICO}" \
    --reader-property parse.key=true --reader-property key.separator='|' 2> /dev/null
}

sql() {
  docker exec "$(container mysql)" mysql -uroot -proot123 download_db -e "$1" 2> /dev/null
}

sql_valor() {
  docker exec "$(container mysql)" mysql -uroot -proot123 download_db -N -e "$1" 2> /dev/null
}

# logs_replicas <padrao>: linhas dos logs de todas as réplicas desde o início do script, com o nome da réplica
logs_replicas() {
  for r in $(replicas); do
    docker logs --since "$INICIO" "$r" 2>&1 | grep -E "$1" | sed -E "s/^.*(CatalogService|KafkaErrorHandlingConfig) *: /[$(nome_curto "$r")] /" || true
  done
}

# status_http <url>: código HTTP da resposta (000 se não houver resposta em 5 s)
status_http() {
  curl -s -m 5 -w '\n%{http_code}' "$1" 2> /dev/null | tail -1 || echo "000"
}

# instancia_http <url>: valor do header X-Instance-Id da resposta
instancia_http() {
  curl -s -m 5 -D - "$1" 2> /dev/null | grep -i '^x-instance-id:' | tr -d '\r' | cut -d' ' -f2 || true
}

# aguardar <segundos> <comando...>: repete o comando até ele dar certo ou o tempo acabar
aguardar() {
  local limite=$1
  shift
  for _ in $(seq 1 "$limite"); do
    if "$@" > /dev/null 2>&1; then
      return 0
    fi
    sleep 1
  done
  return 1
}
