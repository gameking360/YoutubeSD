package br.furb.youtubesd.download.catalog;

import br.furb.youtubesd.download.TestcontainersConfiguration;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.context.annotation.Import;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.kafka.core.KafkaTemplate;

import java.time.Duration;
import java.util.UUID;

import static org.assertj.core.api.Assertions.assertThat;
import static org.awaitility.Awaitility.await;

@SpringBootTest
@AutoConfigureMockMvc
@Import(TestcontainersConfiguration.class)
class VideoRemovedConsumerTest {

    private static final Duration TIMEOUT = Duration.ofSeconds(30);

    @Autowired
    private KafkaTemplate<String, String> kafka;

    @Autowired
    private JdbcClient jdbc;

    @Test
    void remocaoMarcaOVideoComoRemovido() throws Exception {
        publicar("rem-1", UUID.randomUUID().toString(), "2026-11-20T10:00:00Z");
        await().atMost(TIMEOUT).untilAsserted(() -> assertThat(status("rem-1")).isEqualTo("PUBLISHED"));

        remover("rem-1", UUID.randomUUID().toString(), "2026-11-20T10:05:00Z");

        await().atMost(TIMEOUT).untilAsserted(() -> assertThat(status("rem-1")).isEqualTo("REMOVED"));
        assertThat(removidoEm("rem-1")).isNotNull();
    }

    @Test
    void remocaoRepetidaGeraUmUnicoEfeito() throws Exception {
        publicar("rem-2", UUID.randomUUID().toString(), "2026-11-20T10:00:00Z");
        await().atMost(TIMEOUT).untilAsserted(() -> assertThat(status("rem-2")).isEqualTo("PUBLISHED"));

        String repetida = UUID.randomUUID().toString();
        for (int i = 0; i < 3; i++) {
            remover("rem-2", repetida, "2026-11-20T10:05:00Z");
        }

        await().atMost(TIMEOUT).untilAsserted(() -> assertThat(status("rem-2")).isEqualTo("REMOVED"));
        await().atMost(TIMEOUT).untilAsserted(() -> assertThat(vezesRegistrada(repetida)).isEqualTo(1));
    }

    @Test
    void remocaoAntigaNaoDesfazUmaRepublicacaoMaisNova() throws Exception {
        publicar("rem-3", UUID.randomUUID().toString(), "2026-11-20T10:00:00Z");
        String republicacao = UUID.randomUUID().toString();
        publicar("rem-3", republicacao, "2026-11-20T10:10:00Z");
        await().atMost(TIMEOUT).untilAsserted(() -> assertThat(vezesRegistrada(republicacao)).isEqualTo(1));

        String antiga = UUID.randomUUID().toString();
        remover("rem-3", antiga, "2026-11-20T10:05:00Z");

        await().atMost(TIMEOUT).untilAsserted(() -> assertThat(vezesRegistrada(antiga)).isEqualTo(1));
        assertThat(status("rem-3")).isEqualTo("PUBLISHED");
    }

    @Test
    void remocaoDeVideoInexistenteNaoFalha() throws Exception {
        String messageId = UUID.randomUUID().toString();
        remover("nunca-publicado", messageId, "2026-11-20T10:05:00Z");

        await().atMost(TIMEOUT).untilAsserted(() -> assertThat(vezesRegistrada(messageId)).isEqualTo(1));
        assertThat(status("nunca-publicado")).isNull();
    }

    private void publicar(String videoId, String messageId, String occurredAt) throws Exception {
        String json = """
                {
                  "messageId": "%s", "correlationId": "%s", "type": "VideoPublished", "version": 1,
                  "occurredAt": "%s",
                  "payload": {
                    "videoId": "%s", "ownerId": "dono-1", "title": "Vídeo %s", "visibility": "PUBLIC",
                    "durationSec": 600, "hlsBucket": "videos-hls", "masterKey": "%s/master.m3u8",
                    "renditions": [{"name": "360p", "width": 640, "height": 360, "bandwidth": 800000}],
                    "publishedAt": "%s"
                  }
                }
                """.formatted(messageId, videoId, occurredAt, videoId, videoId, videoId, occurredAt);
        kafka.send("video.published", videoId, json).get();
    }

    private void remover(String videoId, String messageId, String occurredAt) throws Exception {
        String json = """
                {
                  "messageId": "%s", "correlationId": "%s", "type": "VideoRemoved", "version": 1,
                  "occurredAt": "%s",
                  "payload": { "videoId": "%s", "reason": "Removido pelo dono", "removedAt": "%s" }
                }
                """.formatted(messageId, videoId, occurredAt, videoId, occurredAt);
        kafka.send("video.removed", videoId, json).get();
    }

    private String status(String videoId) {
        return jdbc.sql("SELECT status FROM video_catalog WHERE video_id = :id")
                .param("id", videoId).query(String.class).optional().orElse(null);
    }

    private Object removidoEm(String videoId) {
        return jdbc.sql("SELECT removed_at FROM video_catalog WHERE video_id = :id")
                .param("id", videoId).query(Object.class).single();
    }

    private int vezesRegistrada(String messageId) {
        return jdbc.sql("SELECT COUNT(*) FROM processed_messages WHERE message_id = :id")
                .param("id", messageId).query(Integer.class).single();
    }
}
