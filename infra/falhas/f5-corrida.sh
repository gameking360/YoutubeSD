#!/bin/bash
# F5: 100 requisições simultâneas de upload, pelo gateway, para um usuário com 10 vagas de cota.
# Exatamente 10 reservam a vaga; as outras 90 são recusadas pelo Identity e compensadas pela saga.
source "$(dirname "$0")/upload-comum.sh"
exigir kafka mysql ministack

QTD=${QTD:-100}
RUN=$(date +%s)
EMAIL="f5-$RUN@teste.local"
SENHA="SenhaF5-$RUN"
DIR=$(mktemp -d)
trap 'rm -rf "$DIR"; caos_reset' EXIT

titulo "F5 — Corrida pelo recurso: $QTD requisições simultâneas para 10 vagas"
echo "Esperado: as $QTD requisições recebem 202; o Identity reserva exatamente 10 vagas;"
echo "as outras 90 são recusadas e a saga compensa cada uma (FAILED, envio abortado, cliente notificado)."

passo "Criando um usuário novo (cota padrão de 10 vídeos)"
USER_ID=$(curl -s -m 10 -X POST "$GATEWAY/api/identity/users" -H 'Content-Type: application/json' \
  -d "{\"name\":\"Teste F5 $RUN\",\"email\":\"$EMAIL\",\"password\":\"$SENHA\"}" | sed -n 's/.*"id":"\([^"]*\)".*/\1/p')
TOKEN=$(curl -s -m 10 -X POST "$GATEWAY/api/auth/login" -H 'Content-Type: application/json' \
  -d "{\"email\":\"$EMAIL\",\"password\":\"$SENHA\"}" | sed -n 's/.*"accessToken":"\([^"]*\)".*/\1/p')
[ -n "$USER_ID" ] && [ -n "$TOKEN" ] || { echo "Não foi possível criar o usuário ou fazer login."; exit 1; }
echo "userId=$USER_ID"
sql_db identity_db "SELECT quota_limit, used, reserved FROM quotas WHERE user_id = '$USER_ID'"

passo "Disparando $QTD POST /api/upload/videos ao mesmo tempo, às $(date +%T)"
for i in $(seq 1 "$QTD"); do
  curl -s -i -m 60 -X POST "$GATEWAY/api/upload/videos" \
    -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
    -d "{\"fileName\":\"f5-$i.mp4\",\"sizeBytes\":1048576,\"title\":\"F5 $i\"}" > "$DIR/r$i" &
done
wait
codigos() { for f in "$DIR"/r*; do head -1 "$f" | tr -d '\r' | cut -d' ' -f2; done; }
echo "respostas HTTP:"
codigos | sort | uniq -c | sed 's/^/  /'
echo "réplica do Upload que atendeu cada requisição:"
cat "$DIR"/r* | grep -ai '^x-instance-id:' | tr -d '\r' | cut -d' ' -f2 | sort | uniq -c | sed 's/^/  /'
ACEITAS=$(codigos | grep -c '^202$' || true)

passo "Aguardando o Identity responder os $QTD pedidos de cota"
resolvidos() { [ "$(sql_db_valor upload_db "SELECT COUNT(*) FROM video_upload WHERE owner_id = '$USER_ID' AND quota_state = 'REQUESTED'")" = "0" ]; }
aguardar 120 resolvidos || true

passo "Cota no Identity (identity_db)"
sql_db identity_db "SELECT quota_limit, used, reserved FROM quotas WHERE user_id = '$USER_ID'"
sql_db identity_db "SELECT status, COUNT(*) AS reservas FROM quota_reservations WHERE user_id = '$USER_ID' GROUP BY status"
RESERVADO=$(sql_db_valor identity_db "SELECT reserved FROM quotas WHERE user_id = '$USER_ID'")

passo "Estado da saga no Upload (upload_db)"
sql_db upload_db "SELECT status, quota_state, IFNULL(failure_reason, '-') AS motivo, upload_state, COUNT(*) AS videos FROM video_upload WHERE owner_id = '$USER_ID' GROUP BY 1, 2, 3, 4"
SUCESSOS=$(sql_db_valor upload_db "SELECT COUNT(*) FROM video_upload WHERE owner_id = '$USER_ID' AND quota_state = 'RESERVED'")
RECUSADAS=$(sql_db_valor upload_db "SELECT COUNT(*) FROM video_upload WHERE owner_id = '$USER_ID' AND status = 'FAILED' AND quota_state = 'REJECTED'")
NOTIFICADAS=$(sql_db_valor upload_db "SELECT COUNT(*) FROM user_notification WHERE user_id = '$USER_ID' AND type = 'VIDEO_FAILED'")
echo "sucessos (vaga reservada): $SUCESSOS, recusadas e compensadas: $RECUSADAS, notificações de falha: $NOTIFICADAS"

passo "Limpeza: cancelando os envios que reservaram vaga (a vaga volta para o usuário)"
for v in $(sql_db_valor upload_db "SELECT video_id FROM video_upload WHERE owner_id = '$USER_ID' AND quota_state = 'RESERVED'"); do
  curl -s -m 10 -X DELETE "$GATEWAY/api/upload/videos/$v" -H "Authorization: Bearer $TOKEN" > /dev/null || true
done
devolvidas() { [ "$(sql_db_valor identity_db "SELECT reserved FROM quotas WHERE user_id = '$USER_ID'")" = "0" ]; }
aguardar 60 devolvidas || true
sql_db identity_db "SELECT quota_limit, used, reserved FROM quotas WHERE user_id = '$USER_ID'"

if [ "$ACEITAS" = "$QTD" ] && [ "$SUCESSOS" = "10" ] && [ "$RESERVADO" = "10" ] && [ "$RECUSADAS" = "$((QTD - 10))" ]; then
  resultado ok "$QTD requisições simultâneas: exatamente 10 vagas reservadas e $RECUSADAS recusadas e compensadas."
else
  resultado falhou "aceitas=$ACEITAS sucessos=$SUCESSOS reservado_no_identity=$RESERVADO recusadas=$RECUSADAS"
fi
