CREATE TABLE IF NOT EXISTS users (
  id CHAR(36) NOT NULL,
  name VARCHAR(120) NOT NULL,
  email VARCHAR(255) NOT NULL,
  password_hash VARCHAR(255) NOT NULL,
  role VARCHAR(40) NOT NULL DEFAULT 'USER',
  created_at DATETIME(6) NOT NULL,
  updated_at DATETIME(6) NOT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY ux_users_email (email)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS quotas (
  user_id CHAR(36) NOT NULL,
  quota_limit INT NOT NULL,
  used INT NOT NULL DEFAULT 0,
  reserved INT NOT NULL DEFAULT 0,
  updated_at DATETIME(6) NOT NULL,
  PRIMARY KEY (user_id),
  CONSTRAINT fk_quotas_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE,
  CONSTRAINT ck_quotas_used CHECK (used >= 0),
  CONSTRAINT ck_quotas_reserved CHECK (reserved >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS quota_reservations (
  id CHAR(36) NOT NULL,
  video_id VARCHAR(128) NOT NULL,
  user_id CHAR(36) NOT NULL,
  correlation_id VARCHAR(128) NOT NULL,
  status VARCHAR(20) NOT NULL,
  created_at DATETIME(6) NOT NULL,
  updated_at DATETIME(6) NOT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY ux_quota_reservations_video (video_id),
  KEY ix_quota_reservations_user (user_id),
  CONSTRAINT fk_quota_reservations_user FOREIGN KEY (user_id) REFERENCES users (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS processed_messages (
  message_id CHAR(36) NOT NULL,
  consumer VARCHAR(80) NOT NULL,
  topic VARCHAR(120) NOT NULL,
  processed_at DATETIME(6) NOT NULL,
  PRIMARY KEY (message_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS outbox (
  id BIGINT NOT NULL AUTO_INCREMENT,
  message_id CHAR(36) NOT NULL,
  topic VARCHAR(120) NOT NULL,
  message_key VARCHAR(128) NOT NULL,
  type VARCHAR(80) NOT NULL,
  payload JSON NOT NULL,
  correlation_id VARCHAR(128) NOT NULL,
  occurred_at DATETIME(6) NOT NULL,
  created_at DATETIME(6) NOT NULL,
  published_at DATETIME(6) NULL,
  attempts INT NOT NULL DEFAULT 0,
  last_error VARCHAR(500) NULL,
  PRIMARY KEY (id),
  UNIQUE KEY ux_outbox_message (message_id),
  KEY ix_outbox_pending (published_at, id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
