package br.furb.youtubesd.download.progress;

import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.testcontainers.service.connection.ServiceConnection;
import org.testcontainers.mysql.MySQLContainer;

import java.time.Instant;

import static org.assertj.core.api.Assertions.assertThat;

@SpringBootTest
class ProgressRepositoryTest {

    @ServiceConnection
    static MySQLContainer mysql = new MySQLContainer("mysql:9.7");

    static {
        mysql.start();
    }

    private static final Instant T1 = Instant.parse("2026-11-19T10:00:00.000Z");
    private static final Instant T2 = Instant.parse("2026-11-19T10:00:10.000Z");

    @Autowired
    private ProgressRepository repository;

    @Test
    void salvaPrimeiroProgresso() {
        repository.save(progress("u1", 30, T1));

        assertThat(repository.find("u1", "v1"))
                .contains(progress("u1", 30, T1));
    }

    @Test
    void escritaMaisRecenteSobrescreve() {
        repository.save(progress("u2", 30, T1));
        repository.save(progress("u2", 90, T2));

        assertThat(repository.find("u2", "v1"))
                .contains(progress("u2", 90, T2));
    }

    @Test
    void escritaAntigaNaoSobrescreve() {
        repository.save(progress("u3", 90, T2));
        repository.save(progress("u3", 30, T1));

        assertThat(repository.find("u3", "v1"))
                .contains(progress("u3", 90, T2));
    }

    @Test
    void buscaInexistenteRetornaVazio() {
        assertThat(repository.find("ninguem", "v1")).isEmpty();
    }

    private static PlaybackProgress progress(String userId, int positionSec, Instant clientTs) {
        return new PlaybackProgress(userId, "v1", positionSec, 600, PlaybackStatus.IN_PROGRESS, clientTs);
    }
}
