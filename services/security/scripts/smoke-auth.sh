#!/usr/bin/env bash
# End-to-end authentication through YARP. Requires curl and the running environment.
set -euo pipefail
GATEWAY="${GATEWAY_URL:-http://localhost:8080}"
RUN="$(date +%s)-$RANDOM"
RESPONSE="$(mktemp)"
trap 'rm -f "$RESPONSE"' EXIT

request() {
  local expected="$1" method="$2" path="$3"
  shift 3
  local status
  status=$(curl -sS --max-time 30 -o "$RESPONSE" -w '%{http_code}' -X "$method" "$GATEWAY$path" "$@")
  if [ "$status" != "$expected" ]; then
    echo "FALHOU: $method $path retornou $status; esperado $expected."
    exit 1
  fi
}
field() { sed -n "s/.*\"$1\":\"\([^\"]*\)\".*/\1/p" "$RESPONSE"; }

echo "1/7 Cadastro e login pelo gateway..."
request 201 POST /api/identity/users -H 'Content-Type: application/json' \
  -d "{\"name\":\"Security smoke\",\"email\":\"security-$RUN@teste.local\",\"password\":\"senha123\"}"
USER_ID=$(field id)
[ -n "$USER_ID" ]
request 401 POST /api/auth/login -H 'Content-Type: application/json' \
  -d "{\"email\":\"security-$RUN@teste.local\",\"password\":\"incorreta\"}"
request 200 POST /api/auth/login -H 'Content-Type: application/json' \
  -d "{\"email\":\"security-$RUN@teste.local\",\"password\":\"senha123\"}"
ACCESS=$(field accessToken)
REFRESH=$(field refreshToken)
[ -n "$ACCESS" ] && [ -n "$REFRESH" ]

echo "2/7 JWKS público e integração interna bloqueada..."
request 200 GET /api/auth/.well-known/jwks.json
if ! grep -q '"kty":"RSA"' "$RESPONSE" || grep -q '"d":' "$RESPONSE"; then exit 1; fi
request 404 POST /api/identity/internal/authenticate -H 'Content-Type: application/json' -d '{}'

echo "3/7 Identity valida JWT e ignora identidade forjada por header..."
request 401 GET /api/identity/users/me -H "X-User-Id: $USER_ID"
request 200 GET /api/identity/users/me -H "Authorization: Bearer $ACCESS" -H 'X-User-Id: forged-user'
[ "$(field id)" = "$USER_ID" ]
request 200 GET /api/identity/quota/me -H "Authorization: Bearer $ACCESS"
request 401 POST "/api/identity/admin/users/$USER_ID/quota/reset"

echo "4/7 Download Java valida o token emitido pelo C#..."
request 401 GET /api/download/progress -H "X-User-Id: $USER_ID"
request 401 GET /api/download/progress -H 'Authorization: Bearer invalid'
request 200 PUT "/api/download/progress/security-smoke-$RUN" \
  -H "Authorization: Bearer $ACCESS" -H 'Content-Type: application/json' \
  -d "{\"positionSec\":42,\"durationSec\":600,\"clientTs\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\"}"
request 200 GET "/api/download/progress/security-smoke-$RUN" -H "Authorization: Bearer $ACCESS"
grep -q '"positionSec":42' "$RESPONSE"

echo "5/7 Rotação do refresh token..."
request 200 POST /api/auth/refresh -H 'Content-Type: application/json' -d "{\"refreshToken\":\"$REFRESH\"}"
NEXT_REFRESH=$(field refreshToken)
NEXT_ACCESS=$(field accessToken)
[ -n "$NEXT_REFRESH" ] && [ "$NEXT_REFRESH" != "$REFRESH" ]
request 200 GET /api/identity/users/me -H "Authorization: Bearer $NEXT_ACCESS"

echo "6/7 Reutilização revoga a sessão..."
request 401 POST /api/auth/refresh -H 'Content-Type: application/json' -d "{\"refreshToken\":\"$REFRESH\"}"
request 401 POST /api/auth/refresh -H 'Content-Type: application/json' -d "{\"refreshToken\":\"$NEXT_REFRESH\"}"

echo "7/7 Access token já emitido segue válido até expirar..."
request 200 GET /api/identity/users/me -H "Authorization: Bearer $NEXT_ACCESS"
echo "RESULTADO: OK — login, JWT C#/Java, JWKS, rotação e rejeição de reutilização."
