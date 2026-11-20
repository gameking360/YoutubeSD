package br.furb.youtubesd.download.catalog;

import br.furb.youtubesd.download.messaging.MessageParser;
import org.apache.kafka.clients.consumer.ConsumerRecord;
import org.springframework.kafka.annotation.KafkaListener;
import org.springframework.stereotype.Component;

@Component
public class VideoEventsConsumer {

    private final MessageParser parser;
    private final CatalogService service;

    public VideoEventsConsumer(MessageParser parser, CatalogService service) {
        this.parser = parser;
        this.service = service;
    }

    @KafkaListener(topics = "${download.topics.video-published}")
    public void onVideoPublished(ConsumerRecord<String, String> record) {
        service.applyPublished(parser.parse(record.value(), "VideoPublished", 1, VideoPublishedPayload.class));
    }
}
