package br.furb.youtubesd.download.progress;

import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.stereotype.Repository;

import java.sql.ResultSet;
import java.sql.SQLException;
import java.time.Instant;
import java.time.LocalDateTime;
import java.time.ZoneOffset;
import java.time.temporal.ChronoUnit;
import java.util.List;
import java.util.Optional;

@Repository
public class ProgressRepository {

    private static final String UPSERT = """
            INSERT INTO playback_progress (user_id, video_id, position_sec, duration_sec, status, client_ts)
            VALUES (:userId, :videoId, :positionSec, :durationSec, :status, :clientTs) AS novo
            ON DUPLICATE KEY UPDATE
                position_sec = IF(novo.client_ts > playback_progress.client_ts, novo.position_sec, playback_progress.position_sec),
                duration_sec = IF(novo.client_ts > playback_progress.client_ts, novo.duration_sec, playback_progress.duration_sec),
                status       = IF(novo.client_ts > playback_progress.client_ts, novo.status, playback_progress.status),
                client_ts    = GREATEST(novo.client_ts, playback_progress.client_ts)
            """;

    private static final String FIND = """
            SELECT user_id, video_id, position_sec, duration_sec, status, client_ts
            FROM playback_progress
            WHERE user_id = :userId AND video_id = :videoId
            """;

    private static final String FIND_BY_STATUS = """
            SELECT user_id, video_id, position_sec, duration_sec, status, client_ts
            FROM playback_progress
            WHERE user_id = :userId AND status = :status
            ORDER BY client_ts DESC
            LIMIT :limit
            """;

    private final JdbcClient jdbc;

    public ProgressRepository(JdbcClient jdbc) {
        this.jdbc = jdbc;
    }

    public void save(PlaybackProgress progress) {
        jdbc.sql(UPSERT)
                .param("userId", progress.userId())
                .param("videoId", progress.videoId())
                .param("positionSec", progress.positionSec())
                .param("durationSec", progress.durationSec())
                .param("status", progress.status().name())
                .param("clientTs", toDatabase(progress.clientTs()))
                .update();
    }

    public Optional<PlaybackProgress> find(String userId, String videoId) {
        return jdbc.sql(FIND)
                .param("userId", userId)
                .param("videoId", videoId)
                .query(this::map)
                .optional();
    }

    public List<PlaybackProgress> findByStatus(String userId, PlaybackStatus status, int limit) {
        return jdbc.sql(FIND_BY_STATUS)
                .param("userId", userId)
                .param("status", status.name())
                .param("limit", limit)
                .query(this::map)
                .list();
    }

    private PlaybackProgress map(ResultSet rs, int row) throws SQLException {
        return new PlaybackProgress(
                rs.getString("user_id"),
                rs.getString("video_id"),
                rs.getInt("position_sec"),
                rs.getInt("duration_sec"),
                PlaybackStatus.valueOf(rs.getString("status")),
                rs.getObject("client_ts", LocalDateTime.class).toInstant(ZoneOffset.UTC));
    }

    private static LocalDateTime toDatabase(Instant instant) {
        return LocalDateTime.ofInstant(instant.truncatedTo(ChronoUnit.MILLIS), ZoneOffset.UTC);
    }
}
