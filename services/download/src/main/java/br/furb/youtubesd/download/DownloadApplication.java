package br.furb.youtubesd.download;

import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;
import org.springframework.boot.context.properties.ConfigurationPropertiesScan;

@SpringBootApplication
@ConfigurationPropertiesScan
public class DownloadApplication {

	public static void main(String[] args) {
		SpringApplication.run(DownloadApplication.class, args);
	}

}
