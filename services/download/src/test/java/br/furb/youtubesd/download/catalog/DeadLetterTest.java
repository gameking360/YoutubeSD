package br.furb.youtubesd.download.catalog;

import br.furb.youtubesd.download.TestcontainersConfiguration;
import org.apache.kafka.clients.consumer.Consumer;
import org.apache.kafka.clients.consumer.ConsumerRecord;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.context.annotation.Import;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.kafka.core.ConsumerFactory;
import org.springframework.kafka.core.KafkaTemplate;
import org.springframework.kafka.support.KafkaHeaders;
import org.springframework.kafka.test.utils.KafkaTestUtils;

import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.ArrayList;
import java.util.List;
import java.util.UUID;

import static org.assertj.core.api.Assertions.assertThat;
import static org.awaitility.Awaitility.await;

@SpringBootTest
@AutoConfigureMockMvc
@Import(TestcontainersConfiguration.class)
class DeadLetterTest {

    private static final String TOPIC = "video.published";
    private static final String DLQ = "video.published.dlq";
    private static final Duration TIMEOUT = Duration.ofSeconds(30);

    @Autowired
    private KafkaTemplate<String, String> kafka;

    @Autowired
    private ConsumerFactory<String, String> consumerFactory;

    @Autowired
    private JdbcClient jdbc;

    @Test
    void mensagemForaDoContratoVaiDiretoParaADlqEAsOutrasSeguem() throws Exception {
        kafka.send(TOPIC, "dlq-1", "{ isso não é json").get();
        kafka.send(TOPIC, "dlq-1", mensagemValida("dlq-1", "Depois da venenosa")).get();

        ConsumerRecord<String, String> morta = aguardarNaDlq("dlq-1");
        assertThat(header(morta, KafkaHeaders.DLT_EXCEPTION_MESSAGE)).contains("JSON inválido");
        assertThat(header(morta, KafkaHeaders.DLT_ORIGINAL_TOPIC)).isEqualTo(TOPIC);

        await().atMost(TIMEOUT).untilAsserted(() ->
                assertThat(titulo("dlq-1")).isEqualTo("Depois da venenosa"));
    }

    @Test
    void erroQueNaoEDoContratoETentadoDeNovoAntesDeIrParaADlq() throws Exception {
        String tituloGrandeDemaisParaOBanco = "x".repeat(300);
        long inicio = System.nanoTime();
        kafka.send(TOPIC, "dlq-2", mensagemValida("dlq-2", tituloGrandeDemaisParaOBanco)).get();

        ConsumerRecord<String, String> morta = aguardarNaDlq("dlq-2");
        Duration ateADlq = Duration.ofNanos(System.nanoTime() - inicio);

        assertThat(header(morta, KafkaHeaders.DLT_EXCEPTION_MESSAGE)).contains("Data too long");
        assertThat(ateADlq).isGreaterThanOrEqualTo(Duration.ofSeconds(3));
        assertThat(titulo("dlq-2")).isNull();
    }

    @Test
    void versaoDesconhecidaVaiDiretoParaADlq() throws Exception {
        String v3 = mensagemValida("dlq-3", "Versão do futuro").replace("\"version\": 1,", "\"version\": 3,");
        kafka.send(TOPIC, "dlq-3", v3).get();

        ConsumerRecord<String, String> morta = aguardarNaDlq("dlq-3");

        assertThat(header(morta, KafkaHeaders.DLT_EXCEPTION_MESSAGE)).contains("v3 não é suportado");
        assertThat(titulo("dlq-3")).isNull();
    }

    private ConsumerRecord<String, String> aguardarNaDlq(String key) {
        List<ConsumerRecord<String, String>> recebidas = new ArrayList<>();
        try (Consumer<String, String> consumer = consumerFactory.createConsumer("dlq-" + UUID.randomUUID(), null)) {
            consumer.subscribe(List.of(DLQ));
            await().atMost(TIMEOUT).until(() -> {
                KafkaTestUtils.getRecords(consumer, Duration.ofSeconds(1)).forEach(recebidas::add);
                return recebidas.stream().anyMatch(r -> key.equals(r.key()));
            });
        }
        return recebidas.stream().filter(r -> key.equals(r.key())).findFirst().orElseThrow();
    }

    private static String header(ConsumerRecord<String, String> record, String name) {
        return new String(record.headers().lastHeader(name).value(), StandardCharsets.UTF_8);
    }

    private String titulo(String videoId) {
        return jdbc.sql("SELECT title FROM video_catalog WHERE video_id = :id")
                .param("id", videoId).query(String.class).optional().orElse(null);
    }

    private static String mensagemValida(String videoId, String title) {
        return """
                {
                  "messageId": "%s",
                  "correlationId": "%s",
                  "type": "VideoPublished",
                  "version": 1,
                  "occurredAt": "2026-11-20T10:00:00Z",
                  "payload": {
                    "videoId": "%s",
                    "ownerId": "dono-1",
                    "title": "%s",
                    "visibility": "PUBLIC",
                    "durationSec": 600,
                    "hlsBucket": "videos-hls",
                    "masterKey": "%s/master.m3u8",
                    "renditions": [{"name": "360p", "width": 640, "height": 360, "bandwidth": 800000}],
                    "publishedAt": "2026-11-20T10:00:00Z"
                  }
                }
                """.formatted(UUID.randomUUID(), videoId, videoId, title, videoId);
    }
}
