package br.furb.youtubesd.download.progress;

import br.furb.youtubesd.download.TestcontainersConfiguration;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.context.annotation.Import;
import org.springframework.data.redis.core.StringRedisTemplate;

import java.time.Instant;
import java.util.concurrent.TimeUnit;

import static org.assertj.core.api.Assertions.assertThat;

@SpringBootTest
@AutoConfigureMockMvc
@Import(TestcontainersConfiguration.class)
class ProgressCacheTest {

    private static final Instant T1 = Instant.parse("2026-11-19T10:00:00.000Z");
    private static final Instant T2 = Instant.parse("2026-11-19T10:00:10.000Z");
    private static final long ONE_HOUR_MS = TimeUnit.HOURS.toMillis(1);

    @Autowired
    private ProgressCache cache;

    @Autowired
    private ProgressService service;

    @Autowired
    private ProgressRepository repository;

    @Autowired
    private StringRedisTemplate redis;

    @Test
    void gravaComTtlDeUmaHora() {
        cache.put(progress("r1", 30, T1));

        long ttl = redis.getExpire(ProgressCache.key("r1", "v1"), TimeUnit.MILLISECONDS);
        assertThat(ttl).isPositive().isLessThanOrEqualTo(ONE_HOUR_MS);
        assertThat(cache.get("r1", "v1")).contains(progress("r1", 30, T1));
    }

    @Test
    void escritaAntigaNaoRegrideOCache() {
        cache.put(progress("r2", 90, T2));
        cache.put(progress("r2", 30, T1));

        assertThat(cache.get("r2", "v1")).contains(progress("r2", 90, T2));
    }

    @Test
    void escritaMaisRecenteAtualizaOCache() {
        cache.put(progress("r3", 30, T1));
        cache.put(progress("r3", 90, T2));

        assertThat(cache.get("r3", "v1")).contains(progress("r3", 90, T2));
    }

    @Test
    void leituraSemCacheBuscaNoBancoEPreencheOCache() {
        repository.save(progress("r4", 45, T1));
        assertThat(cache.get("r4", "v1")).isEmpty();

        assertThat(service.get("r4", "v1")).isEqualTo(progress("r4", 45, T1));
        assertThat(cache.get("r4", "v1")).contains(progress("r4", 45, T1));
    }

    @Test
    void leituraComCacheNaoPrecisaDoBanco() {
        cache.put(progress("r5", 60, T1));

        assertThat(service.get("r5", "v1")).isEqualTo(progress("r5", 60, T1));
    }

    @Test
    void escritaPeloServicoAtualizaOCache() {
        service.save("r6", "v1", new ProgressRequest(120, 600, T1));

        assertThat(cache.get("r6", "v1")).contains(progress("r6", 120, T1));
    }

    private static PlaybackProgress progress(String userId, int positionSec, Instant clientTs) {
        return new PlaybackProgress(userId, "v1", positionSec, 600, PlaybackStatus.IN_PROGRESS, clientTs);
    }
}
