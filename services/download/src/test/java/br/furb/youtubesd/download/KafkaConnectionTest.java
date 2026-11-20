package br.furb.youtubesd.download;

import org.apache.kafka.clients.consumer.Consumer;
import org.apache.kafka.clients.consumer.ConsumerRecord;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.context.annotation.Import;
import org.springframework.kafka.core.ConsumerFactory;
import org.springframework.kafka.core.KafkaTemplate;
import org.springframework.kafka.test.utils.KafkaTestUtils;

import java.time.Duration;
import java.util.List;

import static org.assertj.core.api.Assertions.assertThat;

@SpringBootTest
@AutoConfigureMockMvc
@Import(TestcontainersConfiguration.class)
class KafkaConnectionTest {

    @Autowired
    private KafkaTemplate<String, String> kafkaTemplate;

    @Autowired
    private ConsumerFactory<String, String> consumerFactory;

    @Test
    void publicaELeUmaMensagem() throws Exception {
        kafkaTemplate.send("teste.conexao", "chave-1", "mensagem de teste").get();

        try (Consumer<String, String> consumer = consumerFactory.createConsumer("teste-conexao", null)) {
            consumer.subscribe(List.of("teste.conexao"));
            ConsumerRecord<String, String> record =
                    KafkaTestUtils.getSingleRecord(consumer, "teste.conexao", Duration.ofSeconds(30));

            assertThat(record.key()).isEqualTo("chave-1");
            assertThat(record.value()).isEqualTo("mensagem de teste");
        }
    }
}
