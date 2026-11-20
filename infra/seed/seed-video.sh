#!/bin/bash
# Gera um vídeo HLS (360p e 720p), envia ao MiniStack e publica o VideoPublished no Kafka.
# Uso: ./infra/seed/seed-video.sh [videoId] [titulo] [arquivo]   (com o AppHost rodando)
#   sem [arquivo]: gera um vídeo de teste de 20 s
#   com [arquivo]: converte o seu vídeo (mp4, mov, mkv...)
set -euo pipefail
export MSYS_NO_PATHCONV=1

VIDEO_ID="${1:-demo-1}"
TITLE="${2:-Video de demonstracao}"
SOURCE_FILE="${3:-}"
BUCKET="${HLS_BUCKET:-videos-hls}"
TOPIC="${KAFKA_TOPIC_VIDEO_PUBLISHED:-video.published}"
FFMPEG_IMAGE="jrottenberg/ffmpeg:9.0-ubuntu"
WORK="seed-ffmpeg-$VIDEO_ID"
PROBE="seed-ffprobe-$VIDEO_ID"

find_container() {
  docker ps --format '{{.Names}}' | grep -E "^$1-" | grep -v ui | head -1 || true
}

even() {
  echo $(( ($1 + 1) / 2 * 2 ))
}

MINISTACK=$(find_container ministack)
KAFKA=$(find_container kafka)
if [ -z "$MINISTACK" ] || [ -z "$KAFKA" ]; then
  echo "MiniStack e Kafka precisam estar rodando. Suba o AppHost antes."
  exit 1
fi
docker rm -f "$WORK" "$PROBE" > /dev/null 2>&1 || true

if [ -n "$SOURCE_FILE" ]; then
  if [ ! -f "$SOURCE_FILE" ]; then
    echo "Arquivo não encontrado: $SOURCE_FILE"
    exit 1
  fi
  HOST_FILE="$SOURCE_FILE"
  if command -v cygpath > /dev/null; then
    HOST_FILE=$(cygpath -w "$SOURCE_FILE")
  fi

  echo "0/3 Analisando $SOURCE_FILE ..."
  docker create --name "$PROBE" --entrypoint ffprobe "$FFMPEG_IMAGE" \
    -v error -show_entries stream=codec_type,width,height:format=duration \
    -of default=noprint_wrappers=1 /tmp/input > /dev/null
  docker cp "$HOST_FILE" "$PROBE:/tmp/input"
  INFO=$(docker start -a "$PROBE")
  docker rm "$PROBE" > /dev/null

  DURATION=$(echo "$INFO" | grep '^duration=' | head -1 | cut -d= -f2 | cut -d. -f1)
  SRC_WIDTH=$(echo "$INFO" | grep '^width=' | head -1 | cut -d= -f2)
  SRC_HEIGHT=$(echo "$INFO" | grep '^height=' | head -1 | cut -d= -f2)
  HAS_AUDIO=$(echo "$INFO" | grep -c '^codec_type=audio' || true)
  if [ -z "$SRC_WIDTH" ] || [ -z "$SRC_HEIGHT" ]; then
    echo "O arquivo não tem uma faixa de vídeo."
    exit 1
  fi
  [ "${DURATION:-0}" -ge 1 ] || DURATION=1
  INPUTS=(-i /tmp/input)
  AUDIO="0:a:0"
else
  DURATION=20
  SRC_WIDTH=1280
  SRC_HEIGHT=720
  HAS_AUDIO=1
  INPUTS=(-f lavfi -i "testsrc2=size=1280x720:rate=30:duration=$DURATION"
          -f lavfi -i "sine=frequency=440:duration=$DURATION")
  AUDIO="1:a"
fi

WIDTH_360=$(even $(( SRC_WIDTH * 360 / SRC_HEIGHT )))
WIDTH_720=$(even $(( SRC_WIDTH * 720 / SRC_HEIGHT )))

if [ "$HAS_AUDIO" -gt 0 ]; then
  MAPS=(-map "[v360]" -map "$AUDIO" -map "[v720]" -map "$AUDIO" -c:a aac -b:a 128k)
  STREAM_MAP="v:0,a:0,name:360p v:1,a:1,name:720p"
else
  MAPS=(-map "[v360]" -map "[v720]")
  STREAM_MAP="v:0,name:360p v:1,name:720p"
fi

echo "1/3 Gerando HLS de ${DURATION}s em 360p e 720p..."
docker create --name "$WORK" "$FFMPEG_IMAGE" \
  -hide_banner -loglevel error \
  "${INPUTS[@]}" \
  -filter_complex "[0:v]split=2[a][b];[a]scale=$WIDTH_360:360[v360];[b]scale=$WIDTH_720:720[v720]" \
  "${MAPS[@]}" \
  -c:v libx264 -preset veryfast -sc_threshold 0 -force_key_frames "expr:gte(t,n_forced*4)" \
  -b:v:0 800k -b:v:1 2800k \
  -f hls -hls_time 4 -hls_playlist_type vod \
  -hls_segment_filename "/tmp/out/%v/seg_%05d.ts" \
  -master_pl_name master.m3u8 \
  -var_stream_map "$STREAM_MAP" \
  "/tmp/out/%v/index.m3u8" > /dev/null
if [ -n "$SOURCE_FILE" ]; then
  docker cp "$HOST_FILE" "$WORK:/tmp/input"
fi
docker start -a "$WORK"

echo "2/3 Enviando para s3://$BUCKET/$VIDEO_ID/ ..."
docker exec "$MINISTACK" rm -rf /tmp/out
docker cp "$WORK:/tmp/out" - | docker cp - "$MINISTACK:/tmp"
docker rm "$WORK" > /dev/null
docker exec \
  -e AWS_ACCESS_KEY_ID=test -e AWS_SECRET_ACCESS_KEY=test -e AWS_DEFAULT_REGION=us-east-1 \
  "$MINISTACK" sh -c "aws --endpoint-url http://localhost:4566 s3 cp --recursive --only-show-errors /tmp/out s3://$BUCKET/$VIDEO_ID/ && rm -rf /tmp/out"

echo "3/3 Publicando VideoPublished em $TOPIC ..."
MESSAGE_ID=$(docker exec "$KAFKA" cat /proc/sys/kernel/random/uuid)
NOW=$(date -u +%Y-%m-%dT%H:%M:%SZ)
RENDITIONS="[{\"name\":\"360p\",\"width\":$WIDTH_360,\"height\":360,\"bandwidth\":928000},{\"name\":\"720p\",\"width\":$WIDTH_720,\"height\":720,\"bandwidth\":2928000}]"
PAYLOAD="{\"messageId\":\"$MESSAGE_ID\",\"correlationId\":\"$VIDEO_ID\",\"type\":\"VideoPublished\",\"version\":1,\"occurredAt\":\"$NOW\",\"payload\":{\"videoId\":\"$VIDEO_ID\",\"ownerId\":\"seed\",\"title\":\"$TITLE\",\"visibility\":\"PUBLIC\",\"durationSec\":$DURATION,\"hlsBucket\":\"$BUCKET\",\"masterKey\":\"$VIDEO_ID/master.m3u8\",\"renditions\":$RENDITIONS,\"publishedAt\":\"$NOW\"}}"
echo "$VIDEO_ID|$PAYLOAD" | docker exec -i "$KAFKA" kafka-console-producer \
  --bootstrap-server localhost:9092 --topic "$TOPIC" \
  --reader-property parse.key=true --reader-property key.separator='|'

echo "Pronto: videoId=$VIDEO_ID messageId=$MESSAGE_ID (${DURATION}s, 360p ${WIDTH_360}x360, 720p ${WIDTH_720}x720)"
