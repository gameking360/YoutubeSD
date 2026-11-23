#!/bin/bash
# F5: 100 pedidos de reserva para 10 vagas geram exatamente 10 aceitos e 90 rejeitados.
# Requer o ambiente no ar (Aspire ou Compose) e o Identity consumindo o Kafka.
# Uso: ./services/identity/scripts/f5-cota.sh
set -euo pipefail
export MSYS_NO_PATHCONV=1

IDENTITY_URL="${IDENTITY_URL:-http://localhost:8080}"
MYSQL="${MYSQL_CONTAINER:-}"
KAFKA="${KAFKA_CONTAINER:-}"
ADMIN_TOKEN="${IDENTITY_ADMIN_TOKEN:-}"
RUN="$(date +%s)"
EMAIL="f5-$RUN@teste.local"

find_container() {
  docker ps --format '{{.Names}}	{{.Label "com.docker.compose.service"}}' | awk -F'\t' -v s="$1" '
    $2 == s { print $1; exit }
    $2 == "" && index($1, s "-") == 1 && $1 !~ /-ui-/ { print $1; exit }' || true
}

[ -n "$MYSQL" ] || MYSQL="$(find_container mysql)"
[ -n "$KAFKA" ] || KAFKA="$(find_container kafka)"

if [ -z "$MYSQL" ] || [ -z "$KAFKA" ]; then
  echo "MySQL e Kafka precisam estar rodando. Suba o AppHost ou o Docker Compose antes."
  exit 1
fi

uuid() {
  printf '%08x-%04x-4%03x-%04x-%04x%04x%04x' \
    $((RANDOM * 32768 + RANDOM)) $RANDOM $((RANDOM & 0x0FFF)) $RANDOM $RANDOM $RANDOM $RANDOM
}

sql_valor() {
  docker exec "$MYSQL" mysql -uroot -proot123 identity_db -N -e "$1" 2> /dev/null
}

echo "0/4 Criando usuário de teste em $IDENTITY_URL ..."
RESPOSTA=$(curl -s -m 10 -X POST "$IDENTITY_URL/api/identity/users" \
  -H 'Content-Type: application/json' \
  -d "{\"name\":\"Teste F5 $RUN\",\"email\":\"$EMAIL\",\"password\":\"senha123\"}")

USER_ID=$(echo "$RESPOSTA" | sed -n 's/.*"id":"\([^"]*\)".*/\1/p')
if [ -z "$USER_ID" ]; then
  echo "Falha ao criar o usuário: $RESPOSTA"
  exit 1
fi
echo "    userId=$USER_ID"

curl -s -m 10 -X POST "$IDENTITY_URL/api/identity/admin/users/$USER_ID/quota/reset" \
  -H "X-Admin-Token: $ADMIN_TOKEN" > /dev/null

echo "1/4 Publicando 100 pedidos de reserva ..."
AGORA=$(date -u +%Y-%m-%dT%H:%M:%SZ)
{
  for i in $(seq 1 100); do
    VIDEO_ID="f5-$RUN-$i"
    MESSAGE_ID=$(uuid)
    printf '%s|{"messageId":"%s","correlationId":"%s","type":"QuotaReserveRequested","version":1,"occurredAt":"%s","payload":{"videoId":"%s","ownerId":"%s"}}\n' \
      "$VIDEO_ID" "$MESSAGE_ID" "$VIDEO_ID" "$AGORA" "$VIDEO_ID" "$USER_ID"
  done
} | docker exec -i "$KAFKA" kafka-console-producer \
  --bootstrap-server localhost:9092 --topic quota.reserve-requested \
  --reader-property parse.key=true --reader-property key.separator='|' 2> /dev/null

echo "2/4 Aguardando o processamento ..."
RESERVADOS=0
REJEITADOS=0
for _ in $(seq 1 60); do
  RESERVADOS=$(sql_valor "SELECT COUNT(*) FROM quota_reservations WHERE user_id = '$USER_ID' AND status = 'RESERVED'")
  REJEITADOS=$(sql_valor "SELECT COUNT(*) FROM outbox WHERE topic = 'quota.rejected' AND JSON_UNQUOTE(JSON_EXTRACT(payload, '$.payload.ownerId')) = '$USER_ID'")
  if [ "$RESERVADOS" = "10" ] && [ "$REJEITADOS" = "90" ]; then
    break
  fi
  sleep 1
done

echo "3/4 Estado do banco:"
sql_valor "SELECT quota_limit, used, reserved FROM quotas WHERE user_id = '$USER_ID'" | sed 's/^/    quotas(limit,used,reserved): /'
echo "    reservas ativas: $RESERVADOS"
echo "    rejeições: $REJEITADOS"

echo "4/4 Eventos no outbox:"
sql_valor "SELECT topic, COUNT(*) FROM outbox WHERE topic IN ('quota.reserved','quota.rejected') AND JSON_UNQUOTE(JSON_EXTRACT(payload, '$.payload.ownerId')) = '$USER_ID' GROUP BY topic" | sed 's/^/    /'

if [ "$RESERVADOS" = "10" ] && [ "$REJEITADOS" = "90" ]; then
  echo "RESULTADO: OK — 10 aceitos e 90 rejeitados."
else
  echo "RESULTADO: FALHOU — reservados=$RESERVADOS rejeitados=$REJEITADOS"
  exit 1
fi
