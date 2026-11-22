#!/bin/bash
set -e

BOOTSTRAP="${KAFKA_BOOTSTRAP_SERVERS:-kafka:9093}"
PARTITIONS="${KAFKA_TOPIC_PARTITIONS:-3}"

TOPICS="
quota.reserve-requested
quota.reserved
quota.rejected
quota.release-requested
video.uploaded
video.processed
video.processing-failed
video.published
video.removed
"

until kafka-topics --bootstrap-server "$BOOTSTRAP" --list > /dev/null 2>&1; do
  echo "Aguardando o Kafka em $BOOTSTRAP..."
  sleep 2
done

for topic in $TOPICS; do
  for name in "$topic" "$topic.dlq"; do
    kafka-topics --bootstrap-server "$BOOTSTRAP" --create --if-not-exists \
      --topic "$name" --partitions "$PARTITIONS" --replication-factor 1
  done
done

echo "Tópicos disponíveis:"
kafka-topics --bootstrap-server "$BOOTSTRAP" --list
