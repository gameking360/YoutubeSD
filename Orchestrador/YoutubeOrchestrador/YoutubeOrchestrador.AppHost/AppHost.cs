var builder = DistributedApplication.CreateBuilder(args);

var redisPassword = builder.AddParameter("redis-password", "root123", secret: true);

var redis = builder.AddRedis("redis", port: 6379, password: redisPassword)
    .WithDataVolume("youtubesd-redis-data")
    .WithPersistence(TimeSpan.FromSeconds(10))
    .WithLifetime(ContainerLifetime.Persistent);

var kafka = builder.AddKafka("kafka", port: 9092)
    .WithDataVolume("youtubesd-kafka-data")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithKafkaUI(ui => ui
        .WithHostPort(8090)
        .WithLifetime(ContainerLifetime.Persistent));







builder.Build().Run();