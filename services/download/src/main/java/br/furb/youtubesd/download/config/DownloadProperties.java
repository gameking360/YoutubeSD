package br.furb.youtubesd.download.config;

import org.springframework.boot.context.properties.ConfigurationProperties;

@ConfigurationProperties(prefix = "download")
public record DownloadProperties(String instanceId) {
}
