package br.furb.youtubesd.download.storage;

import br.furb.youtubesd.download.error.ResourceNotFoundException;
import org.springframework.stereotype.Component;
import software.amazon.awssdk.core.ResponseInputStream;
import software.amazon.awssdk.services.s3.S3Client;
import software.amazon.awssdk.services.s3.model.GetObjectResponse;
import software.amazon.awssdk.services.s3.model.NoSuchKeyException;

@Component
public class HlsStorage {

    private final S3Client s3;

    public HlsStorage(S3Client s3) {
        this.s3 = s3;
    }

    public byte[] read(String bucket, String key) {
        try {
            return s3.getObjectAsBytes(request -> request.bucket(bucket).key(key)).asByteArray();
        } catch (NoSuchKeyException ex) {
            throw notFound(key);
        }
    }

    public ResponseInputStream<GetObjectResponse> open(String bucket, String key) {
        try {
            return s3.getObject(request -> request.bucket(bucket).key(key));
        } catch (NoSuchKeyException ex) {
            throw notFound(key);
        }
    }

    private static ResourceNotFoundException notFound(String key) {
        return new ResourceNotFoundException("Arquivo HLS não encontrado: " + key);
    }
}
