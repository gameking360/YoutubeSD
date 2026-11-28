package br.furb.youtubesd.download.catalog;

import br.furb.youtubesd.download.messaging.InvalidMessageException;
import br.furb.youtubesd.download.messaging.MessageEnvelope;
import br.furb.youtubesd.download.messaging.MessageParser;
import br.furb.youtubesd.download.messaging.ParsedMessage;
import org.springframework.stereotype.Component;

/**
 * Lê o VideoPublished nas versões que o Download conhece. A v1 e a v2 convivem no mesmo tópico;
 * uma versão desconhecida vira mensagem fora do contrato e vai para a DLQ.
 */
@Component
public class VideoPublishedReader {

    static final String TYPE = "VideoPublished";

    private final MessageParser parser;

    public VideoPublishedReader(MessageParser parser) {
        this.parser = parser;
    }

    public ParsedMessage<VideoPublishedPayload> read(String json) {
        MessageEnvelope envelope = parser.envelope(json);

        if (!TYPE.equals(envelope.type())) {
            throw new InvalidMessageException("Esperado " + TYPE + ", recebido " + envelope.type());
        }

        VideoPublishedPayload payload = switch (envelope.version()) {
            case 1 -> parser.payload(envelope, VideoPublishedPayload.class);
            case 2 -> parser.payload(envelope, VideoPublishedV2Payload.class).toV1();
            default -> throw new InvalidMessageException(
                    TYPE + " v" + envelope.version() + " não é suportado (versões aceitas: 1 e 2)");
        };

        return new ParsedMessage<>(envelope, payload);
    }
}
