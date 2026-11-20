package br.furb.youtubesd.download.progress;

import br.furb.youtubesd.download.config.DownloadProperties;
import org.junit.jupiter.api.Test;
import org.springframework.data.redis.RedisConnectionFailureException;
import org.springframework.data.redis.core.StringRedisTemplate;
import org.springframework.data.redis.core.script.RedisScript;

import java.time.Duration;
import java.time.Instant;
import java.util.List;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatCode;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.ArgumentMatchers.anyList;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

class ProgressCacheUnavailableTest {

    private final StringRedisTemplate redis = mock(StringRedisTemplate.class);
    private final ProgressCache cache = new ProgressCache(redis, new DownloadProperties("teste", Duration.ofHours(1)));

    @Test
    void leituraComRedisForaDoArRetornaVazio() {
        when(redis.opsForHash()).thenThrow(new RedisConnectionFailureException("Redis fora do ar"));

        assertThat(cache.get("u1", "v1")).isEmpty();
    }

    @Test
    void gravacaoComRedisForaDoArNaoFalhaETentaRemoverAChave() {
        when(redis.execute(any(RedisScript.class), anyList(), any(Object[].class)))
                .thenThrow(new RedisConnectionFailureException("Redis fora do ar"));
        when(redis.delete(ProgressCache.key("u1", "v1")))
                .thenThrow(new RedisConnectionFailureException("Redis fora do ar"));

        PlaybackProgress progress = new PlaybackProgress(
                "u1", "v1", 30, 600, PlaybackStatus.IN_PROGRESS, Instant.parse("2026-11-19T10:00:00Z"));

        assertThatCode(() -> cache.put(progress)).doesNotThrowAnyException();
        verify(redis).delete(ProgressCache.key("u1", "v1"));
    }
}
