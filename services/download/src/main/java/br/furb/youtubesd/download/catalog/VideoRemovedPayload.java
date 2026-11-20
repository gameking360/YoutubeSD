package br.furb.youtubesd.download.catalog;

import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.NotNull;

import java.time.Instant;

public record VideoRemovedPayload(
        @NotBlank String videoId,
        String reason,
        @NotNull Instant removedAt) {
}
