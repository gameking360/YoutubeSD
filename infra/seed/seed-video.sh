#!/bin/bash
# Gera um vídeo HLS de teste, envia ao MiniStack e publica o VideoPublished no Kafka.
# Uso: ./infra/seed/seed-video.sh [videoId] [titulo]   (com o AppHost rodando)
set -euo pipefail
export MSYS_NO_PATHCONV=1

VIDEO_ID="${1:-demo-1}"
TITLE="${2:-Video de demonstracao}"
DURATION=20
BUCKET="${HLS_BUCKET:-videos-hls}"
TOPIC="${KAFKA_TOPIC_VIDEO_PUBLISHED:-video.published}"
FFMPEG_IMAGE="jrottenberg/ffmpeg:9.0-ubuntu"
WORK="seed-ffmpeg-$VIDEO_ID"

find_container() {
  docker ps --format '{{.Names}}' | grep -E "^$1-" | grep -v ui | head -1 || true
}

MINISTACK=$(find_container ministack)
KAFKA=$(find_container kafka)
if [ -z "$MINISTACK" ] || [ -z "$KAFKA" ]; then
  echo "MiniStack e Kafka precisam estar rodando. Suba o AppHost antes."
  exit 1
fi

echo "1/3 Gerando HLS de ${DURATION}s em 360p e 720p..."
docker rm -f "$WORK" > /dev/null 2>&1 || true
docker run --name "$WORK" "$FFMPEG_IMAGE" \
  -hide_banner -loglevel error \
  -f lavfi -i "testsrc2=size=1280x720:rate=30:duration=$DURATION" \
  -f lavfi -i "sine=frequency=440:duration=$DURATION" \
  -filter_complex "[0:v]split=2[a][b];[a]scale=640:360[v360];[b]scale=1280:720[v720]" \
  -map "[v360]" -map 1:a -map "[v720]" -map 1:a \
  -c:v libx264 -preset veryfast -g 120 -keyint_min 120 -sc_threshold 0 \
  -b:v:0 800k -b:v:1 2800k -c:a aac -b:a 128k \
  -f hls -hls_time 4 -hls_playlist_type vod \
  -hls_segment_filename "/tmp/out/%v/seg_%05d.ts" \
  -master_pl_name master.m3u8 \
  -var_stream_map "v:0,a:0,name:360p v:1,a:1,name:720p" \
  "/tmp/out/%v/index.m3u8"

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
PAYLOAD="{\"messageId\":\"$MESSAGE_ID\",\"correlationId\":\"$VIDEO_ID\",\"type\":\"VideoPublished\",\"version\":1,\"occurredAt\":\"$NOW\",\"payload\":{\"videoId\":\"$VIDEO_ID\",\"ownerId\":\"seed\",\"title\":\"$TITLE\",\"visibility\":\"PUBLIC\",\"durationSec\":$DURATION,\"hlsBucket\":\"$BUCKET\",\"masterKey\":\"$VIDEO_ID/master.m3u8\",\"renditions\":[{\"name\":\"360p\",\"width\":640,\"height\":360,\"bandwidth\":928000},{\"name\":\"720p\",\"width\":1280,\"height\":720,\"bandwidth\":2928000}],\"publishedAt\":\"$NOW\"}}"
echo "$VIDEO_ID|$PAYLOAD" | docker exec -i "$KAFKA" kafka-console-producer \
  --bootstrap-server localhost:9092 --topic "$TOPIC" \
  --reader-property parse.key=true --reader-property key.separator='|'

echo "Pronto: videoId=$VIDEO_ID messageId=$MESSAGE_ID"
