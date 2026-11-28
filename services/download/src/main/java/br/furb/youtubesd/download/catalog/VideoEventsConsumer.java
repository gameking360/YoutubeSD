package br.furb.youtubesd.download.catalog;

import br.furb.youtubesd.download.messaging.MessageParser;
import org.apache.kafka.clients.consumer.ConsumerRecord;
import org.springframework.kafka.annotation.KafkaListener;
import org.springframework.stereotype.Component;

@Component
public class VideoEventsConsumer {

    private final MessageParser parser;
    private final VideoPublishedReader publishedReader;
    private final CatalogService service;

    public VideoEventsConsumer(MessageParser parser, VideoPublishedReader publishedReader, CatalogService service) {
        this.parser = parser;
        this.publishedReader = publishedReader;
        this.service = service;
    }

    @KafkaListener(topics = "${download.topics.video-published}")
    public void onVideoPublished(ConsumerRecord<String, String> record) {
        service.applyPublished(publishedReader.read(record.value()));
    }

    @KafkaListener(topics = "${download.topics.video-removed}")
    public void onVideoRemoved(ConsumerRecord<String, String> record) {
        service.applyRemoved(parser.parse(record.value(), "VideoRemoved", 1, VideoRemovedPayload.class));
    }
}
