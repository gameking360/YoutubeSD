package br.furb.youtubesd.download.catalog;

import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.Positive;

public record Rendition(
        @NotBlank String name,
        @Positive int width,
        @Positive int height,
        @Positive int bandwidth) {
}
