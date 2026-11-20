package br.furb.youtubesd.download.progress;

import br.furb.youtubesd.download.error.ResourceNotFoundException;
import org.springframework.stereotype.Service;

import java.util.Optional;

@Service
public class ProgressService {

    private static final int COMPLETED_PERCENT = 95;

    private final ProgressRepository repository;
    private final ProgressCache cache;

    public ProgressService(ProgressRepository repository, ProgressCache cache) {
        this.repository = repository;
        this.cache = cache;
    }

    public PlaybackProgress save(String userId, String videoId, ProgressRequest request) {
        repository.save(new PlaybackProgress(
                userId,
                videoId,
                request.positionSec(),
                request.durationSec(),
                statusOf(request.positionSec(), request.durationSec()),
                request.clientTs()));

        PlaybackProgress stored = findInDatabase(userId, videoId);
        cache.put(stored);
        return stored;
    }

    public PlaybackProgress get(String userId, String videoId) {
        Optional<PlaybackProgress> cached = cache.get(userId, videoId);
        if (cached.isPresent()) {
            return cached.get();
        }

        PlaybackProgress stored = findInDatabase(userId, videoId);
        cache.put(stored);
        return stored;
    }

    private PlaybackProgress findInDatabase(String userId, String videoId) {
        return repository.find(userId, videoId)
                .orElseThrow(() -> new ResourceNotFoundException(
                        "Nenhum progresso encontrado para o vídeo " + videoId));
    }

    private static PlaybackStatus statusOf(int positionSec, int durationSec) {
        boolean completed = positionSec * 100L >= durationSec * (long) COMPLETED_PERCENT;
        return completed ? PlaybackStatus.COMPLETED : PlaybackStatus.IN_PROGRESS;
    }
}
