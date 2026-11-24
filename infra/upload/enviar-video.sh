#!/bin/bash
# Envia um vídeo pelo gateway em partes, do mesmo jeito para vídeo curto ou longo.
# Se uma réplica do Upload cair, a parte é repetida e o gateway manda para outra. Se nenhuma
# estiver disponível, o script espera até alguma voltar e continua de onde parou.
#
# Uso (com o ambiente no ar, na raiz do repositório):
#   ./infra/upload/enviar-video.sh <arquivo> [titulo] [PUBLIC|UNLISTED|PRIVATE]
#   ./infra/upload/enviar-video.sh --gerar 20 [titulo]       # gera um vídeo de teste de 20 s
#
# Autenticação: TOKEN=<jwt>, ou EMAIL e SENHA (o usuário é cadastrado se ainda não existir).
# Retomar um envio interrompido: RETOMAR=<videoId> ./infra/upload/enviar-video.sh <arquivo>
set -euo pipefail
export MSYS_NO_PATHCONV=1

GATEWAY="${GATEWAY_URL:-http://localhost:8080}"
EMAIL="${EMAIL:-demo-upload@youtubesd.local}"
SENHA="${SENHA:-SenhaDemo123!}"
TMP=$(mktemp -d)
# No Git Bash o curl é nativo do Windows: usa o caminho no formato C:/... que todos entendem.
if command -v cygpath > /dev/null; then
  TMP=$(cygpath -m "$TMP")
fi
trap 'rm -rf "$TMP"' EXIT

if [ "${1:-}" = "--gerar" ]; then
  DURACAO="${2:-20}"
  ARQUIVO="$TMP/teste-${DURACAO}s.mp4"
  TITULO="${3:-Vídeo de teste de ${DURACAO}s}"
  echo "Gerando vídeo de teste de ${DURACAO}s com FFmpeg num container..."
  docker run --rm -v "$TMP:/out" jrottenberg/ffmpeg:9.0-ubuntu \
    -hide_banner -loglevel error -f lavfi -i "testsrc2=size=1280x720:rate=30:duration=$DURACAO" \
    -f lavfi -i "sine=frequency=440:duration=$DURACAO" -c:v libx264 -preset ultrafast -c:a aac -shortest \
    "/out/teste-${DURACAO}s.mp4"
  VISIBILIDADE="${4:-PUBLIC}"
else
  ARQUIVO="${1:?Uso: $0 <arquivo> [titulo] [visibilidade] | --gerar <segundos> [titulo]}"
  TITULO="${2:-$(basename "$ARQUIVO")}"
  VISIBILIDADE="${3:-PUBLIC}"
fi

[ -f "$ARQUIVO" ] || { echo "Arquivo não encontrado: $ARQUIVO"; exit 1; }
TAMANHO=$(wc -c < "$ARQUIVO" | tr -d ' ')
NOME=$(basename "$ARQUIVO")

campo() { sed -n "s/.*\"$1\":\"\{0,1\}\([^,\"}]*\)\"\{0,1\}.*/\1/p" | head -1; }

# requisicao <metodo> <url> [arquivo-do-corpo] [content-type]
# Repete sem limite enquanto não houver réplica disponível (rede, 502, 503, 504).
requisicao() {
  local metodo=$1 url=$2 corpo=${3:-} tipo=${4:-application/json} espera=1 codigo
  while true; do
    local args=(-s -m 60 --connect-timeout 5 -X "$metodo" -D "$TMP/headers" -o "$TMP/resposta" -w '%{http_code}' -H "Authorization: Bearer $TOKEN")
    if [ -n "$corpo" ]; then
      args+=(-H "Content-Type: $tipo" --data-binary "@$corpo")
    fi
    # Sem resposta (réplica caiu no meio, gateway fora), o curl já escreve 000.
    codigo=$(curl "${args[@]}" "$url" 2> /dev/null) || true
    codigo=${codigo:-000}
    case "$codigo" in
      000|502|503|504)
        echo "   ... sem réplica do Upload disponível (HTTP $codigo); nova tentativa em ${espera}s" >&2
        sleep "$espera"
        espera=$(( espera * 2 > 30 ? 30 : espera * 2 ))
        ;;
      *)
        echo "$codigo"
        return 0
        ;;
    esac
  done
}

instancia() { tr -d '\r' < "$TMP/headers" | sed -n 's/^[Xx]-[Ii]nstance-[Ii]d: //p' | head -1; }

if [ -z "${TOKEN:-}" ]; then
  printf '{"name":"Demo Upload","email":"%s","password":"%s"}' "$EMAIL" "$SENHA" > "$TMP/cadastro.json"
  curl -s -o /dev/null -X POST -H "Content-Type: application/json" --data-binary "@$TMP/cadastro.json" "$GATEWAY/api/identity/users" || true
  printf '{"email":"%s","password":"%s"}' "$EMAIL" "$SENHA" > "$TMP/login.json"
  TOKEN=$(curl -s -X POST -H "Content-Type: application/json" --data-binary "@$TMP/login.json" "$GATEWAY/api/auth/login" | campo accessToken)
  [ -n "$TOKEN" ] || { echo "Login falhou para $EMAIL."; exit 1; }
fi

if [ -n "${RETOMAR:-}" ]; then
  VIDEO_ID="$RETOMAR"
  codigo=$(requisicao GET "$GATEWAY/api/upload/videos/$VIDEO_ID")
  [ "$codigo" = "200" ] || { echo "Vídeo $VIDEO_ID não encontrado (HTTP $codigo)."; exit 1; }
  echo "Retomando o envio de $VIDEO_ID"
else
  printf '{"title":"%s","visibility":"%s","fileName":"%s","sizeBytes":%s,"contentType":"video/mp4"}' \
    "$TITULO" "$VISIBILIDADE" "$NOME" "$TAMANHO" > "$TMP/criar.json"
  codigo=$(requisicao POST "$GATEWAY/api/upload/videos" "$TMP/criar.json")
  [ "$codigo" = "202" ] || { echo "Falha ao criar o upload (HTTP $codigo): $(cat "$TMP/resposta")"; exit 1; }
  VIDEO_ID=$(campo videoId < "$TMP/resposta")
  echo "Upload criado: videoId=$VIDEO_ID ($TAMANHO bytes) por $(instancia)"
  requisicao GET "$GATEWAY/api/upload/videos/$VIDEO_ID" > /dev/null
fi

PARTE=$(campo chunkSize < "$TMP/resposta")
TOTAL=$(campo totalParts < "$TMP/resposta")
RECEBIDAS=" $(sed -n 's/.*"receivedParts":\[\([^]]*\)\].*/\1/p' "$TMP/resposta" | tr ',' ' ') "
echo "Partes: $TOTAL de até $PARTE bytes"

INICIO=$(date +%s)
for n in $(seq 1 "$TOTAL"); do
  if [[ "$RECEBIDAS" == *" $n "* ]]; then
    echo " parte $n/$TOTAL já estava no servidor"
    continue
  fi
  inicio=$(( (n - 1) * PARTE ))
  tamanho=$(( TAMANHO - inicio < PARTE ? TAMANHO - inicio : PARTE ))
  dd if="$ARQUIVO" of="$TMP/parte" iflag=skip_bytes,count_bytes skip="$inicio" count="$tamanho" status=none
  codigo=$(requisicao PUT "$GATEWAY/api/upload/videos/$VIDEO_ID/parts/$n" "$TMP/parte" application/octet-stream)
  if [ "$codigo" != "200" ]; then
    echo "Parte $n recusada (HTTP $codigo): $(cat "$TMP/resposta")"
    exit 1
  fi
  echo " parte $n/$TOTAL enviada ($tamanho bytes) por $(instancia)"
done

codigo=$(requisicao POST "$GATEWAY/api/upload/videos/$VIDEO_ID/complete")
[ "$codigo" = "202" ] || { echo "Falha ao concluir (HTTP $codigo): $(cat "$TMP/resposta")"; exit 1; }
echo "Envio concluído em $(( $(date +%s) - INICIO ))s. Acompanhando o processamento (Ctrl+C sai sem cancelar)..."

ANTERIOR=""
while true; do
  requisicao GET "$GATEWAY/api/upload/videos/$VIDEO_ID" > /dev/null
  STATUS=$(campo status < "$TMP/resposta")
  ESTADO="$STATUS cota=$(campo quotaState < "$TMP/resposta") processamento=$(campo processingState < "$TMP/resposta")"
  if [ "$ESTADO" != "$ANTERIOR" ]; then
    echo " $(date +%T) $ESTADO $(campo failureReason < "$TMP/resposta" | sed 's/^null$//')"
    ANTERIOR="$ESTADO"
  fi
  case "$STATUS" in
    PUBLISHED)
      echo "Publicado. Reprodução: $GATEWAY/api/download/videos/$VIDEO_ID/playback"
      echo "videoId=$VIDEO_ID"
      exit 0
      ;;
    FAILED)
      echo "Falhou: $(campo failureReason < "$TMP/resposta")"
      echo "videoId=$VIDEO_ID"
      exit 2
      ;;
  esac
  sleep 2
done
