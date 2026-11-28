package br.furb.youtubesd.download.messaging;

import jakarta.validation.ConstraintViolation;
import jakarta.validation.Validator;
import org.springframework.stereotype.Component;
import tools.jackson.core.JacksonException;
import tools.jackson.databind.json.JsonMapper;

import java.util.Set;
import java.util.UUID;
import java.util.stream.Collectors;

@Component
public class MessageParser {

    private final JsonMapper mapper;
    private final Validator validator;

    public MessageParser(JsonMapper mapper, Validator validator) {
        this.mapper = mapper;
        this.validator = validator;
    }

    public <T> ParsedMessage<T> parse(String json, String expectedType, int expectedVersion, Class<T> payloadType) {
        MessageEnvelope envelope = envelope(json);

        if (!expectedType.equals(envelope.type()) || envelope.version() != expectedVersion) {
            throw new InvalidMessageException("Esperado " + expectedType + " v" + expectedVersion
                    + ", recebido " + envelope.type() + " v" + envelope.version());
        }

        return new ParsedMessage<>(envelope, payload(envelope, payloadType));
    }

    public MessageEnvelope envelope(String json) {
        MessageEnvelope envelope = read(json);
        requireValidUuid(envelope.messageId());
        return envelope;
    }

    public <T> T payload(MessageEnvelope envelope, Class<T> payloadType) {
        try {
            return validate(mapper.treeToValue(envelope.payload(), payloadType));
        } catch (JacksonException ex) {
            throw new InvalidMessageException("Payload inválido: " + ex.getOriginalMessage(), ex);
        }
    }

    private MessageEnvelope read(String json) {
        try {
            return validate(mapper.readValue(json, MessageEnvelope.class));
        } catch (JacksonException ex) {
            throw new InvalidMessageException("JSON inválido: " + ex.getOriginalMessage(), ex);
        }
    }

    private <T> T validate(T value) {
        Set<ConstraintViolation<T>> violations = validator.validate(value);
        if (!violations.isEmpty()) {
            String detail = violations.stream()
                    .map(v -> v.getPropertyPath() + " " + v.getMessage())
                    .sorted()
                    .collect(Collectors.joining(", "));
            throw new InvalidMessageException("Mensagem fora do contrato: " + detail);
        }
        return value;
    }

    private static void requireValidUuid(String messageId) {
        try {
            UUID.fromString(messageId);
        } catch (IllegalArgumentException ex) {
            throw new InvalidMessageException("messageId não é um UUID: " + messageId, ex);
        }
    }
}
