package br.furb.youtubesd.download.playback;

import br.furb.youtubesd.download.catalog.CatalogEntry;
import br.furb.youtubesd.download.catalog.CatalogRepository;
import br.furb.youtubesd.download.catalog.Visibility;
import br.furb.youtubesd.download.error.ResourceNotFoundException;
import br.furb.youtubesd.download.progress.PlaybackStatus;
import br.furb.youtubesd.download.progress.ProgressService;
import org.springframework.stereotype.Service;

import java.util.Optional;

@Service
public class PlaybackService {

    private final CatalogRepository catalog;
    private final ProgressService progress;

    public PlaybackService(CatalogRepository catalog, ProgressService progress) {
        this.catalog = catalog;
        this.progress = progress;
    }

    public CatalogEntry accessibleVideo(String videoId, Optional<String> userId) {
        return catalog.findPublished(videoId)
                .filter(video -> canWatch(video, userId))
                .orElseThrow(() -> new ResourceNotFoundException("Vídeo não encontrado: " + videoId));
    }

    public PlaybackSession session(String videoId, Optional<String> userId) {
        CatalogEntry video = accessibleVideo(videoId, userId);
        return new PlaybackSession(
                video.videoId(),
                video.title(),
                video.durationSec(),
                "/api/download/videos/" + video.videoId() + "/hls/master.m3u8",
                userId.map(id -> resumePosition(id, videoId)).orElse(0),
                video.renditions());
    }

    private static boolean canWatch(CatalogEntry video, Optional<String> userId) {
        if (video.visibility() != Visibility.PRIVATE) {
            return true;
        }
        return userId.map(video.ownerId()::equals).orElse(false);
    }

    private int resumePosition(String userId, String videoId) {
        return progress.find(userId, videoId)
                .filter(saved -> saved.status() == PlaybackStatus.IN_PROGRESS)
                .map(saved -> saved.positionSec())
                .orElse(0);
    }
}
