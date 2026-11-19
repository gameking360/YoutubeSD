#!/bin/bash
set -e

BUCKETS="videos-raw videos-hls"

for bucket in $BUCKETS; do
  if ! aws s3api head-bucket --bucket "$bucket" > /dev/null 2>&1; then
    aws s3 mb "s3://$bucket"
  fi
done

echo "Buckets disponíveis:"
aws s3 ls
