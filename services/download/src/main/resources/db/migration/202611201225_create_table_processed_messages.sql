CREATE TABLE processed_messages (
    message_id   VARCHAR(64) NOT NULL,
    type         VARCHAR(64) NOT NULL,
    processed_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    PRIMARY KEY (message_id)
);
