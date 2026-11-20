package br.furb.youtubesd.download.playback;

import br.furb.youtubesd.download.catalog.CatalogEntry;
import br.furb.youtubesd.download.catalog.CatalogRepository;
import br.furb.youtubesd.download.error.ResourceNotFoundException;
import br.furb.youtubesd.download.progress.PlaybackStatus;
import br.furb.youtubesd.download.progress.ProgressService;
import org.springframework.stereotype.Service;

@Service
public class PlaybackService {

    private final CatalogRepository catalog;
    private final ProgressService progress;

    public PlaybackService(CatalogRepository catalog, ProgressService progress) {
        this.catalog = catalog;
        this.progress = progress;
    }

    public CatalogEntry publishedVideo(String videoId) {
        return catalog.findPublished(videoId)
                .orElseThrow(() -> new ResourceNotFoundException("Vídeo não encontrado: " + videoId));
    }

    public PlaybackSession session(String videoId, String userId) {
        CatalogEntry video = publishedVideo(videoId);
        return new PlaybackSession(
                video.videoId(),
                video.title(),
                video.durationSec(),
                "/api/download/videos/" + video.videoId() + "/hls/master.m3u8",
                resumePosition(userId, videoId),
                video.renditions());
    }

    private int resumePosition(String userId, String videoId) {
        if (userId == null || userId.isBlank()) {
            return 0;
        }
        return progress.find(userId, videoId)
                .filter(saved -> saved.status() == PlaybackStatus.IN_PROGRESS)
                .map(saved -> saved.positionSec())
                .orElse(0);
    }
}
