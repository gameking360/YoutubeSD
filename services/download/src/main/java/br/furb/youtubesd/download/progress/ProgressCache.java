package br.furb.youtubesd.download.progress;

import br.furb.youtubesd.download.config.DownloadProperties;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.core.io.ClassPathResource;
import org.springframework.dao.DataAccessException;
import org.springframework.data.redis.core.StringRedisTemplate;
import org.springframework.data.redis.core.script.RedisScript;
import org.springframework.stereotype.Component;

import java.time.Duration;
import java.time.Instant;
import java.util.List;
import java.util.Map;
import java.util.Optional;

@Component
public class ProgressCache {

    private static final Logger log = LoggerFactory.getLogger(ProgressCache.class);

    private static final RedisScript<Long> SAVE_IF_NEWER =
            RedisScript.of(new ClassPathResource("redis/save-progress-if-newer.lua"), Long.class);

    private final StringRedisTemplate redis;
    private final Duration ttl;

    public ProgressCache(StringRedisTemplate redis, DownloadProperties properties) {
        this.redis = redis;
        this.ttl = properties.progressCacheTtl();
    }

    public Optional<PlaybackProgress> get(String userId, String videoId) {
        try {
            Map<Object, Object> fields = redis.opsForHash().entries(key(userId, videoId));
            return fields.isEmpty() ? Optional.empty() : Optional.of(fromHash(userId, videoId, fields));
        } catch (DataAccessException ex) {
            log.warn("Redis indisponível ao ler progresso de {}/{}: {}", userId, videoId, ex.getMessage());
            return Optional.empty();
        }
    }

    public void put(PlaybackProgress progress) {
        String key = key(progress.userId(), progress.videoId());
        try {
            redis.execute(SAVE_IF_NEWER, List.of(key),
                    String.valueOf(progress.positionSec()),
                    String.valueOf(progress.durationSec()),
                    progress.status().name(),
                    String.valueOf(progress.clientTs().toEpochMilli()),
                    String.valueOf(ttl.toMillis()));
        } catch (DataAccessException ex) {
            log.warn("Redis indisponível ao gravar progresso em {}: {}", key, ex.getMessage());
            evict(key);
        }
    }

    static String key(String userId, String videoId) {
        return "download:progress:" + userId + ":" + videoId;
    }

    private void evict(String key) {
        try {
            redis.delete(key);
        } catch (DataAccessException ex) {
            log.warn("Não foi possível remover {} do Redis: {}", key, ex.getMessage());
        }
    }

    private static PlaybackProgress fromHash(String userId, String videoId, Map<Object, Object> fields) {
        return new PlaybackProgress(
                userId,
                videoId,
                Integer.parseInt((String) fields.get("positionSec")),
                Integer.parseInt((String) fields.get("durationSec")),
                PlaybackStatus.valueOf((String) fields.get("status")),
                Instant.ofEpochMilli(Long.parseLong((String) fields.get("clientTs"))));
    }
}
