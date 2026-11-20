package br.furb.youtubesd.download.storage;

import br.furb.youtubesd.download.config.StorageProperties;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.boot.health.contributor.Health;
import org.springframework.boot.health.contributor.HealthIndicator;
import org.springframework.stereotype.Component;
import software.amazon.awssdk.core.exception.SdkException;
import software.amazon.awssdk.services.s3.S3Client;

@Component("s3")
@ConditionalOnProperty(name = "management.health.s3.enabled", matchIfMissing = true)
public class S3HealthIndicator implements HealthIndicator {

    private final S3Client s3;
    private final String bucket;

    public S3HealthIndicator(S3Client s3, StorageProperties storage) {
        this.s3 = s3;
        this.bucket = storage.hlsBucket();
    }

    @Override
    public Health health() {
        try {
            s3.headBucket(request -> request.bucket(bucket));
            return Health.up().withDetail("bucket", bucket).build();
        } catch (SdkException ex) {
            return Health.down().withDetail("bucket", bucket).withDetail("erro", ex.getMessage()).build();
        }
    }
}
