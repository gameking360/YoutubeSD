package br.furb.youtubesd.download.catalog;

import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.stereotype.Repository;
import tools.jackson.databind.json.JsonMapper;

import java.time.Instant;
import java.time.LocalDateTime;
import java.time.ZoneOffset;
import java.time.temporal.ChronoUnit;
import java.util.List;
import java.util.Optional;

@Repository
public class CatalogRepository {

    private static final String UPSERT_PUBLISHED = """
            INSERT INTO video_catalog (video_id, owner_id, title, visibility, duration_sec, hls_bucket, master_key,
                                       renditions, thumbnail_key, status, event_version, published_at, removed_at, last_event_at)
            VALUES (:videoId, :ownerId, :title, :visibility, :durationSec, :hlsBucket, :masterKey,
                    :renditions, :thumbnailKey, 'PUBLISHED', :eventVersion, :publishedAt, NULL, :occurredAt) AS novo
            ON DUPLICATE KEY UPDATE
                owner_id      = IF(novo.last_event_at > video_catalog.last_event_at, novo.owner_id, video_catalog.owner_id),
                title         = IF(novo.last_event_at > video_catalog.last_event_at, novo.title, video_catalog.title),
                visibility    = IF(novo.last_event_at > video_catalog.last_event_at, novo.visibility, video_catalog.visibility),
                duration_sec  = IF(novo.last_event_at > video_catalog.last_event_at, novo.duration_sec, video_catalog.duration_sec),
                hls_bucket    = IF(novo.last_event_at > video_catalog.last_event_at, novo.hls_bucket, video_catalog.hls_bucket),
                master_key    = IF(novo.last_event_at > video_catalog.last_event_at, novo.master_key, video_catalog.master_key),
                renditions    = IF(novo.last_event_at > video_catalog.last_event_at, novo.renditions, video_catalog.renditions),
                thumbnail_key = IF(novo.last_event_at > video_catalog.last_event_at, novo.thumbnail_key, video_catalog.thumbnail_key),
                status        = IF(novo.last_event_at > video_catalog.last_event_at, novo.status, video_catalog.status),
                event_version = IF(novo.last_event_at > video_catalog.last_event_at, novo.event_version, video_catalog.event_version),
                published_at  = IF(novo.last_event_at > video_catalog.last_event_at, novo.published_at, video_catalog.published_at),
                removed_at    = IF(novo.last_event_at > video_catalog.last_event_at, novo.removed_at, video_catalog.removed_at),
                last_event_at = GREATEST(novo.last_event_at, video_catalog.last_event_at)
            """;

    private final JdbcClient jdbc;
    private final JsonMapper mapper;

    public CatalogRepository(JdbcClient jdbc, JsonMapper mapper) {
        this.jdbc = jdbc;
        this.mapper = mapper;
    }

    public boolean upsertPublished(VideoPublishedPayload video, int eventVersion, Instant occurredAt) {
        jdbc.sql(UPSERT_PUBLISHED)
                .param("videoId", video.videoId())
                .param("ownerId", video.ownerId())
                .param("title", video.title())
                .param("visibility", video.visibility().name())
                .param("durationSec", video.durationSec())
                .param("hlsBucket", video.hlsBucket())
                .param("masterKey", video.masterKey())
                .param("renditions", mapper.writeValueAsString(video.renditions()))
                .param("thumbnailKey", video.thumbnailKey())
                .param("eventVersion", eventVersion)
                .param("publishedAt", toDatabase(video.publishedAt()))
                .param("occurredAt", toDatabase(occurredAt))
                .update();

        return lastEventAt(video.videoId())
                .map(stored -> stored.equals(occurredAt.truncatedTo(ChronoUnit.MILLIS)))
                .orElse(false);
    }

    public RemovalResult markRemoved(VideoRemovedPayload removal, Instant occurredAt) {
        int updated = jdbc.sql("""
                        UPDATE video_catalog
                        SET status = 'REMOVED', removed_at = :removedAt, last_event_at = :occurredAt
                        WHERE video_id = :videoId AND last_event_at < :occurredAt
                        """)
                .param("videoId", removal.videoId())
                .param("removedAt", toDatabase(removal.removedAt()))
                .param("occurredAt", toDatabase(occurredAt))
                .update();

        if (updated > 0) {
            return RemovalResult.APPLIED;
        }
        return lastEventAt(removal.videoId()).isPresent() ? RemovalResult.STALE : RemovalResult.NOT_FOUND;
    }

    public List<VideoSummary> findPublic(int offset, int limit) {
        return jdbc.sql("""
                        SELECT video_id, title, duration_sec, thumbnail_key, published_at
                        FROM video_catalog
                        WHERE status = 'PUBLISHED' AND visibility = 'PUBLIC'
                        ORDER BY published_at DESC, video_id
                        LIMIT :limit OFFSET :offset
                        """)
                .param("limit", limit)
                .param("offset", offset)
                .query((rs, row) -> new VideoSummary(
                        rs.getString("video_id"),
                        rs.getString("title"),
                        rs.getInt("duration_sec"),
                        rs.getString("thumbnail_key"),
                        rs.getObject("published_at", LocalDateTime.class).toInstant(ZoneOffset.UTC)))
                .list();
    }

    private Optional<Instant> lastEventAt(String videoId) {
        return jdbc.sql("SELECT last_event_at FROM video_catalog WHERE video_id = :videoId")
                .param("videoId", videoId)
                .query((rs, row) -> rs.getObject("last_event_at", LocalDateTime.class).toInstant(ZoneOffset.UTC))
                .optional();
    }

    private static LocalDateTime toDatabase(Instant instant) {
        return LocalDateTime.ofInstant(instant.truncatedTo(ChronoUnit.MILLIS), ZoneOffset.UTC);
    }
}
