package br.furb.youtubesd.download.catalog;

import java.time.Instant;

public record VideoSummary(
        String videoId,
        String title,
        int durationSec,
        String thumbnailKey,
        Instant publishedAt) {
}
