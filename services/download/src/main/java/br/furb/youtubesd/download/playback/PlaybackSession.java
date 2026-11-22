package br.furb.youtubesd.download.playback;

import br.furb.youtubesd.download.catalog.Rendition;

import java.util.List;

public record PlaybackSession(
        String videoId,
        String title,
        int durationSec,
        String manifestUrl,
        int resumePositionSec,
        List<Rendition> renditions) {
}
