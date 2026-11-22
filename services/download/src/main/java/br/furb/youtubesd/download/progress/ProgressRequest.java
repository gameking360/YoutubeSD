package br.furb.youtubesd.download.progress;

import jakarta.validation.constraints.AssertTrue;
import jakarta.validation.constraints.Min;
import jakarta.validation.constraints.NotNull;
import jakarta.validation.constraints.Positive;

import java.time.Instant;

public record ProgressRequest(
        @Min(0) int positionSec,
        @Positive int durationSec,
        @NotNull Instant clientTs) {

    @AssertTrue(message = "positionSec não pode ser maior que durationSec")
    public boolean isPositionWithinDuration() {
        return positionSec <= durationSec;
    }
}
