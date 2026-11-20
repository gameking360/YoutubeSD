package br.furb.youtubesd.download;

import org.apache.kafka.clients.admin.NewTopic;
import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.kafka.config.TopicBuilder;
import org.springframework.boot.testcontainers.service.connection.ServiceConnection;
import org.springframework.context.annotation.Bean;
import org.testcontainers.containers.GenericContainer;
import org.testcontainers.kafka.ConfluentKafkaContainer;
import org.testcontainers.mysql.MySQLContainer;

@TestConfiguration(proxyBeanMethods = false)
public class TestcontainersConfiguration {

    @Bean
    @ServiceConnection
    MySQLContainer mysqlContainer() {
        return new MySQLContainer("mysql:9.7");
    }

    @Bean
    @ServiceConnection(name = "redis")
    GenericContainer<?> redisContainer() {
        return new GenericContainer<>("redis:8.6").withExposedPorts(6379);
    }

    @Bean
    @ServiceConnection
    ConfluentKafkaContainer kafkaContainer() {
        return new ConfluentKafkaContainer("confluentinc/cp-kafka:8.2.0");
    }

    @Bean
    NewTopic videoPublishedTopic() {
        return TopicBuilder.name("video.published").partitions(3).build();
    }

    @Bean
    NewTopic videoRemovedTopic() {
        return TopicBuilder.name("video.removed").partitions(3).build();
    }

    @Bean
    NewTopic videoPublishedDlqTopic() {
        return TopicBuilder.name("video.published.dlq").partitions(3).build();
    }

    @Bean
    NewTopic videoRemovedDlqTopic() {
        return TopicBuilder.name("video.removed.dlq").partitions(3).build();
    }
}
