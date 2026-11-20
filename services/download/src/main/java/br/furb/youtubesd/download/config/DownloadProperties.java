package br.furb.youtubesd.download.config;

import org.springframework.boot.context.properties.ConfigurationProperties;

import java.time.Duration;

@ConfigurationProperties(prefix = "download")
public record DownloadProperties(String instanceId, Duration progressCacheTtl) {
}
