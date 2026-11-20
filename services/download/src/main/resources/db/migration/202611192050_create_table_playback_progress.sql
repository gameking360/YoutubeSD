CREATE TABLE playback_progress (
    user_id      VARCHAR(64) NOT NULL,
    video_id     VARCHAR(64) NOT NULL,
    position_sec INT         NOT NULL,
    duration_sec INT         NOT NULL,
    status       VARCHAR(20) NOT NULL,
    client_ts    DATETIME(3) NOT NULL,
    updated_at   DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
    PRIMARY KEY (user_id, video_id),
    INDEX idx_playback_progress_continuar (user_id, status, client_ts)
);
