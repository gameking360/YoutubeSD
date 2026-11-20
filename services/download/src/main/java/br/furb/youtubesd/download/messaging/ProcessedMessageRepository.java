package br.furb.youtubesd.download.messaging;

import org.springframework.dao.DuplicateKeyException;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.stereotype.Repository;

@Repository
public class ProcessedMessageRepository {

    private final JdbcClient jdbc;

    public ProcessedMessageRepository(JdbcClient jdbc) {
        this.jdbc = jdbc;
    }

    public boolean register(MessageEnvelope envelope) {
        try {
            jdbc.sql("INSERT INTO processed_messages (message_id, type) VALUES (:messageId, :type)")
                    .param("messageId", envelope.messageId())
                    .param("type", envelope.type())
                    .update();
            return true;
        } catch (DuplicateKeyException ex) {
            return false;
        }
    }
}
