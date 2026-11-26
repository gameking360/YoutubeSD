package br.furb.youtubesd.download.config;

import org.apache.kafka.common.TopicPartition;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.kafka.core.KafkaTemplate;
import org.springframework.kafka.listener.DeadLetterPublishingRecoverer;
import org.springframework.kafka.listener.DefaultErrorHandler;
import org.springframework.util.backoff.FixedBackOff;

@Configuration
public class KafkaErrorHandlingConfig {

    private static final Logger log = LoggerFactory.getLogger(KafkaErrorHandlingConfig.class);

    private static final long RETRY_INTERVAL_MS = 1000;
    private static final long MAX_RETRIES = 3;
    static final String DLQ_SUFFIX = ".dlq";

    @Bean
    public DefaultErrorHandler kafkaErrorHandler(KafkaTemplate<String, String> kafkaTemplate) {
        DeadLetterPublishingRecoverer deadLetter = new DeadLetterPublishingRecoverer(kafkaTemplate,
                (record, ex) -> new TopicPartition(record.topic() + DLQ_SUFFIX, record.partition()));

        DefaultErrorHandler handler = new DefaultErrorHandler((record, ex) -> {
            log.warn("Mensagem enviada para {}{}: chave={} motivo={}",
                    record.topic(), DLQ_SUFFIX, record.key(), rootMessage(ex));
            deadLetter.accept(record, ex);
        }, new FixedBackOff(RETRY_INTERVAL_MS, MAX_RETRIES));

        handler.setRetryListeners((record, ex, attempt) ->
                log.warn("Tentativa {} de {} falhou: topico={} chave={} motivo={}",
                        attempt, MAX_RETRIES + 1, record.topic(), record.key(), rootMessage(ex)));
        return handler;
    }

    private static String rootMessage(Throwable ex) {
        Throwable root = ex;
        while (root.getCause() != null) {
            root = root.getCause();
        }
        return root.getMessage();
    }
}
