package br.furb.youtubesd.download;

import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.context.annotation.Import;
import org.springframework.dao.DuplicateKeyException;
import org.springframework.jdbc.core.simple.JdbcClient;

import java.util.List;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

@SpringBootTest
@AutoConfigureMockMvc
@Import(TestcontainersConfiguration.class)
class DatabaseMigrationTest {

    @Autowired
    private JdbcClient jdbc;

    @Test
    void criaAsTabelasDoServico() {
        List<String> tables = jdbc.sql("""
                        SELECT table_name FROM information_schema.tables
                        WHERE table_schema = DATABASE()
                        """)
                .query(String.class)
                .list();

        assertThat(tables).contains("playback_progress", "video_catalog", "processed_messages");
    }

    @Test
    void mesmoMessageIdNaoPodeSerRegistradoDuasVezes() {
        String insert = "INSERT INTO processed_messages (message_id, type) VALUES (:id, 'VideoPublished')";

        jdbc.sql(insert).param("id", "msg-duplicada").update();

        assertThatThrownBy(() -> jdbc.sql(insert).param("id", "msg-duplicada").update())
                .isInstanceOf(DuplicateKeyException.class);
    }
}
