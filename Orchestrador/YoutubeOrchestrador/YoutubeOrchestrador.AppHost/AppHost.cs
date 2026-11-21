var builder = DistributedApplication.CreateBuilder(args);

var redisPassword = builder.AddParameter("redis-password", "root123", secret: true);
var mysqlPassword = builder.AddParameter("mysql-password", "root123", secret: true);

var redis = builder.AddRedis("redis", port: 6379, password: redisPassword)
    .WithoutHttpsCertificate()
    .WithContainerRuntimeArgs("--tmpfs", "/data")
    .WithLifetime(ContainerLifetime.Persistent);

var kafka = builder.AddKafka("kafka", port: 9092)
    .WithDataVolume("youtubesd-kafka-data")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithKafkaUI(ui => ui
        .WithHostPort(8090)
        .WithLifetime(ContainerLifetime.Persistent));

var kafkaInit = builder.AddContainer("kafka-init", "confluentinc/confluent-local", "8.2.0")
    .WithBindMount("../../../infra/kafka", "/scripts", isReadOnly: true)
    .WithEntrypoint("/bin/bash")
    .WithArgs("/scripts/create-topics.sh")
    .WithEnvironment("KAFKA_BOOTSTRAP_SERVERS", "kafka:9093")
    .WaitFor(kafka);

var mysql = builder.AddMySql("mysql", password: mysqlPassword, port: 3306)
    .WithDataVolume("youtubesd-mysql-data")
    .WithLifetime(ContainerLifetime.Persistent);

var downloadDb = mysql.AddDatabase("downloadDb", "download_db");

var ministack = builder.AddContainer("ministack", "ministackorg/ministack", "1.5.43")
    .WithHttpEndpoint(port: 4566, targetPort: 4566)
    .WithEnvironment("S3_PERSIST", "1")
    .WithEnvironment("S3_DATA_DIR", "/data/s3")
    .WithEnvironment("PERSIST_STATE", "1")
    .WithEnvironment("STATE_DIR", "/data/state")
    .WithVolume("youtubesd-ministack-data", "/data")
    .WithBindMount("../../../infra/ministack/ready.d", "/etc/localstack/init/ready.d", isReadOnly: true)
    .WithHttpHealthCheck("/_ministack/health")
    .WithLifetime(ContainerLifetime.Persistent);

builder.AddProject<Projects.upload>("uploadService")
    .WithHttpHealthCheck("/health");

builder.AddDockerfile("downloadService", "../../../services/download")
    .WithHttpEndpoint(port: 8401, targetPort: 8080)
    .WithHttpHealthCheck("/health")
    .WithEnvironment("INSTANCE_ID", "download-1")
    .WithEnvironment("SPRING_DATASOURCE_URL", "jdbc:mysql://mysql:3306/download_db")
    .WithEnvironment("SPRING_DATASOURCE_USERNAME", "root")
    .WithEnvironment("SPRING_DATASOURCE_PASSWORD", mysqlPassword)
    .WithEnvironment("SPRING_DATA_REDIS_HOST", "redis")
    .WithEnvironment("SPRING_DATA_REDIS_PORT", "6379")
    .WithEnvironment("SPRING_DATA_REDIS_PASSWORD", redisPassword)
    .WithEnvironment("SPRING_KAFKA_BOOTSTRAP_SERVERS", "kafka:9093")
    .WithEnvironment("AWS_ENDPOINT_URL", "http://ministack:4566")
    .WithEnvironment("AWS_REGION", "us-east-1")
    .WithEnvironment("AWS_ACCESS_KEY_ID", "test")
    .WithEnvironment("AWS_SECRET_ACCESS_KEY", "test")
    .WithEnvironment("AUTH_MODE", "header")
    .WaitFor(redis)
    .WaitFor(downloadDb)
    .WaitForCompletion(kafkaInit)
    .WaitFor(ministack);

builder.Build().Run();
