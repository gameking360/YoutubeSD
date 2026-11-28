package br.furb.youtubesd.download.catalog;

import jakarta.validation.Valid;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.NotEmpty;
import jakarta.validation.constraints.NotNull;
import jakarta.validation.constraints.Positive;

import java.time.Instant;
import java.util.List;

/**
 * VideoPublished v2: a localização do HLS fica agrupada em {@code hls} e a duração vem em milissegundos.
 * O catálogo trabalha com o formato da v1, então a v2 é convertida antes de ser aplicada.
 */
public record VideoPublishedV2Payload(
        @NotBlank String videoId,
        @NotBlank String ownerId,
        @NotBlank String title,
        @NotNull Visibility visibility,
        @Positive long durationMs,
        @NotNull @Valid Hls hls,
        String thumbnailKey,
        @NotNull Instant publishedAt) {

    public record Hls(
            @NotBlank String bucket,
            @NotBlank String masterKey,
            @NotEmpty List<@Valid Rendition> renditions) {
    }

    public VideoPublishedPayload toV1() {
        int durationSec = (int) Math.max(1, Math.round(durationMs / 1000.0));
        return new VideoPublishedPayload(videoId, ownerId, title, visibility, durationSec,
                hls.bucket(), hls.masterKey(), hls.renditions(), thumbnailKey, publishedAt);
    }
}
