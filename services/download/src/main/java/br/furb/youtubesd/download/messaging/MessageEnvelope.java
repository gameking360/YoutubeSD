package br.furb.youtubesd.download.messaging;

import jakarta.validation.constraints.Min;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.NotNull;
import tools.jackson.databind.JsonNode;

import java.time.Instant;

public record MessageEnvelope(
        @NotBlank String messageId,
        @NotBlank String correlationId,
        @NotBlank String type,
        @NotNull @Min(1) Integer version,
        @NotNull Instant occurredAt,
        @NotNull JsonNode payload) {
}
