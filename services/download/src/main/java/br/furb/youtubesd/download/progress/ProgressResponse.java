package br.furb.youtubesd.download.progress;

import java.time.Instant;

public record ProgressResponse(
        String videoId,
        int positionSec,
        int durationSec,
        PlaybackStatus status,
        Instant lastWatchedAt) {

    public static ProgressResponse from(PlaybackProgress progress) {
        return new ProgressResponse(
                progress.videoId(),
                progress.positionSec(),
                progress.durationSec(),
                progress.status(),
                progress.clientTs());
    }
}
