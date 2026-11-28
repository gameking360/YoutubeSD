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
class VideoPublishedConsumerTest {

    private static final String TOPIC = "video.published";
    private static final Duration TIMEOUT = Duration.ofSeconds(30);

    @Autowired
    private KafkaTemplate<String, String> kafka;

    @Autowired
    private JdbcClient jdbc;

    @Test
    void mensagemValidaEntraNoCatalogo() throws Exception {
        publicar("video-1", UUID.randomUUID().toString(), "Aula de Kafka", "2026-11-20T10:00:00Z");

        await().atMost(TIMEOUT).untilAsserted(() ->
                assertThat(titulo("video-1")).isEqualTo("Aula de Kafka"));
        assertThat(status("video-1")).isEqualTo("PUBLISHED");
    }

    @Test
    void mesmaMensagemCincoVezesGeraUmUnicoEfeito() throws Exception {
        String repetida = UUID.randomUUID().toString();
        for (int i = 0; i < 5; i++) {
            publicar("video-2", repetida, "Original", "2026-11-20T10:00:00Z");
        }
        publicar("video-2", UUID.randomUUID().toString(), "Sentinela", "2026-11-20T10:00:05Z");

        await().atMost(TIMEOUT).untilAsserted(() ->
                assertThat(titulo("video-2")).isEqualTo("Sentinela"));
        assertThat(vezesRegistrada(repetida)).isEqualTo(1);
        assertThat(linhasNoCatalogo("video-2")).isEqualTo(1);
    }

    @Test
    void eventoAntigoNaoSobrescreveOMaisNovo() throws Exception {
        String antiga = UUID.randomUUID().toString();
        publicar("video-3", UUID.randomUUID().toString(), "Versão nova", "2026-11-20T10:05:00Z");
        publicar("video-3", antiga, "Versão antiga", "2026-11-20T10:00:00Z");

        await().atMost(TIMEOUT).untilAsserted(() ->
                assertThat(vezesRegistrada(antiga)).isEqualTo(1));
        assertThat(titulo("video-3")).isEqualTo("Versão nova");
    }

    @Test
    void v1EV2ConvivemNoMesmoTopico() throws Exception {
        publicar("video-v1", UUID.randomUUID().toString(), "Publicado na v1", "2026-11-28T10:00:00Z");
        publicarV2("video-v2", UUID.randomUUID().toString(), "Publicado na v2", "2026-11-28T10:00:01Z");

        await().atMost(TIMEOUT).untilAsserted(() -> {
            assertThat(titulo("video-v1")).isEqualTo("Publicado na v1");
            assertThat(titulo("video-v2")).isEqualTo("Publicado na v2");
        });
        assertThat(coluna("video-v1", "event_version")).isEqualTo("1");
        assertThat(coluna("video-v2", "event_version")).isEqualTo("2");
        assertThat(coluna("video-v2", "duration_sec")).isEqualTo("600");
        assertThat(coluna("video-v2", "master_key")).isEqualTo("video-v2/master.m3u8");
    }

    private void publicarV2(String videoId, String messageId, String title, String occurredAt) throws Exception {
        String json = """
                {
                  "messageId": "%s",
                  "correlationId": "%s",
                  "type": "VideoPublished",
                  "version": 2,
                  "occurredAt": "%s",
                  "payload": {
                    "videoId": "%s",
                    "ownerId": "dono-1",
                    "title": "%s",
                    "visibility": "PUBLIC",
                    "durationMs": 600000,
                    "hls": {
                      "bucket": "videos-hls",
                      "masterKey": "%s/master.m3u8",
                      "renditions": [{"name": "360p", "width": 640, "height": 360, "bandwidth": 800000}]
                    },
                    "publishedAt": "%s"
                  }
                }
                """.formatted(messageId, videoId, occurredAt, videoId, title, videoId, occurredAt);
        kafka.send(TOPIC, videoId, json).get();
    }

    private String coluna(String videoId, String nome) {
        return jdbc.sql("SELECT CAST(" + nome + " AS CHAR) FROM video_catalog WHERE video_id = :id")
                .param("id", videoId).query(String.class).single();
    }

    private void publicar(String videoId, String messageId, String title, String occurredAt) throws Exception {
        String json = """
                {
                  "messageId": "%s",
                  "correlationId": "%s",
                  "type": "VideoPublished",
                  "version": 1,
                  "occurredAt": "%s",
                  "payload": {
                    "videoId": "%s",
                    "ownerId": "dono-1",
                    "title": "%s",
                    "visibility": "PUBLIC",
                    "durationSec": 600,
                    "hlsBucket": "videos-hls",
                    "masterKey": "%s/master.m3u8",
                    "renditions": [{"name": "360p", "width": 640, "height": 360, "bandwidth": 800000}],
                    "publishedAt": "%s"
                  }
                }
                """.formatted(messageId, videoId, occurredAt, videoId, title, videoId, occurredAt);
        kafka.send(TOPIC, videoId, json).get();
    }

    private String titulo(String videoId) {
        return jdbc.sql("SELECT title FROM video_catalog WHERE video_id = :id")
                .param("id", videoId).query(String.class).optional().orElse(null);
    }

    private String status(String videoId) {
        return jdbc.sql("SELECT status FROM video_catalog WHERE video_id = :id")
                .param("id", videoId).query(String.class).single();
    }

    private int vezesRegistrada(String messageId) {
        return jdbc.sql("SELECT COUNT(*) FROM processed_messages WHERE message_id = :id")
                .param("id", messageId).query(Integer.class).single();
    }

    private int linhasNoCatalogo(String videoId) {
        return jdbc.sql("SELECT COUNT(*) FROM video_catalog WHERE video_id = :id")
                .param("id", videoId).query(Integer.class).single();
    }
}
