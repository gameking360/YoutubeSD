CREATE TABLE video_catalog (
    video_id      VARCHAR(64)  NOT NULL,
    owner_id      VARCHAR(64)  NOT NULL,
    title         VARCHAR(255) NOT NULL,
    visibility    VARCHAR(20)  NOT NULL,
    duration_sec  INT          NOT NULL,
    hls_bucket    VARCHAR(100) NOT NULL,
    master_key    VARCHAR(500) NOT NULL,
    renditions    JSON         NOT NULL,
    thumbnail_key VARCHAR(500) NULL,
    status        VARCHAR(20)  NOT NULL,
    event_version INT          NOT NULL,
    published_at  DATETIME(3)  NOT NULL,
    removed_at    DATETIME(3)  NULL,
    last_event_at DATETIME(3)  NOT NULL,
    updated_at    DATETIME(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
    PRIMARY KEY (video_id),
    INDEX idx_video_catalog_listagem (status, published_at)
);
