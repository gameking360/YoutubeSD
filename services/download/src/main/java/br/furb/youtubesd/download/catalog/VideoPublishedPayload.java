package br.furb.youtubesd.download.catalog;

import jakarta.validation.Valid;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.NotEmpty;
import jakarta.validation.constraints.NotNull;
import jakarta.validation.constraints.Positive;

import java.time.Instant;
import java.util.List;

public record VideoPublishedPayload(
        @NotBlank String videoId,
        @NotBlank String ownerId,
        @NotBlank String title,
        @NotNull Visibility visibility,
        @Positive int durationSec,
        @NotBlank String hlsBucket,
        @NotBlank String masterKey,
        @NotEmpty List<@Valid Rendition> renditions,
        String thumbnailKey,
        @NotNull Instant publishedAt) {
}
