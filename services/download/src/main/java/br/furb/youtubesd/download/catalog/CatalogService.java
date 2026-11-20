package br.furb.youtubesd.download.catalog;

import br.furb.youtubesd.download.config.DownloadProperties;
import br.furb.youtubesd.download.messaging.MessageEnvelope;
import br.furb.youtubesd.download.messaging.ParsedMessage;
import br.furb.youtubesd.download.messaging.ProcessedMessageRepository;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

@Service
public class CatalogService {

    private static final Logger log = LoggerFactory.getLogger(CatalogService.class);

    private final CatalogRepository catalog;
    private final ProcessedMessageRepository processedMessages;
    private final String instanceId;

    public CatalogService(CatalogRepository catalog, ProcessedMessageRepository processedMessages,
                          DownloadProperties properties) {
        this.catalog = catalog;
        this.processedMessages = processedMessages;
        this.instanceId = properties.instanceId();
    }

    @Transactional
    public void applyPublished(ParsedMessage<VideoPublishedPayload> message) {
        MessageEnvelope envelope = message.envelope();
        VideoPublishedPayload video = message.payload();

        if (!processedMessages.register(envelope)) {
            log.info("Mensagem duplicada ignorada: type={} messageId={} correlationId={} instancia={}",
                    envelope.type(), envelope.messageId(), envelope.correlationId(), instanceId);
            return;
        }

        boolean applied = catalog.upsertPublished(video, envelope.version(), envelope.occurredAt());

        log.info("{}: videoId={} messageId={} correlationId={} instancia={}",
                applied ? "VideoPublished aplicado" : "VideoPublished desatualizado ignorado",
                video.videoId(), envelope.messageId(), envelope.correlationId(), instanceId);
    }
}
