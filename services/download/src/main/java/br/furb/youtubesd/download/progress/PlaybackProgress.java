package br.furb.youtubesd.download.progress;

import java.time.Instant;

public record PlaybackProgress(
        String userId,
        String videoId,
        int positionSec,
        int durationSec,
        PlaybackStatus status,
        Instant clientTs) {
}
