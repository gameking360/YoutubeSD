using Aspire.Hosting.Docker.Resources;
using Aspire.Hosting.Docker.Resources.ComposeNodes;
using Aspire.Hosting.Docker.Resources.ServiceNodes;
using Aspire.Hosting.Docker.Resources.ServiceNodes.Swarm;

// Ajustes aplicados ao docker-compose gerado por "aspire publish" (pasta deploy/).
// Cobrem o que o Aspire faz em tempo de execução e não vira Compose sozinho,
// e trocam os ${...} pelos valores de desenvolvimento para o Compose não depender de .env.
static class ComposeAjustes
{
    private const string DotnetDockerfile = "deploy/dotnet.Dockerfile";

    private static readonly Dictionary<string, string> Valores = new()
    {
        ["${MYSQL_PASSWORD}"] = "root123",
        ["${REDIS_PASSWORD}"] = "root123",
        ["${GATEWAY_PORT}"] = "8080",
        ["${UPLOADSERVICE_PORT}"] = "8080",
        ["${SECURITYSERVICE_PORT}"] = "8080",
        ["${IDENTITYSERVICE_PORT}"] = "8080",
        ["${KAFKA_INIT_BINDMOUNT_0}"] = "../infra/kafka",
        ["${MINISTACK_BINDMOUNT_0}"] = "../infra/ministack/ready.d",
    };

    public static void Arquivo(ComposeFile file, int downloadReplicas, int uploadReplicas, int processorReplicas)
    {
        var services = file.Services;

        services["redis"].Tmpfs.Add("/data");

        var mysql = services["mysql"];
        mysql.Environment["MYSQL_DATABASE"] = "download_db";
        mysql.Healthcheck = new Healthcheck
        {
            Test = ["CMD-SHELL", "mysqladmin ping -h localhost -uroot -p$$MYSQL_ROOT_PASSWORD --silent"],
            Interval = "5s",
            Timeout = "3s",
            Retries = 20,
            StartPeriod = "10s",
        };

        for (var i = 1; i <= downloadReplicas; i++)
        {
            var download = services[$"download-{i}"];
            download.Image = "youtubesd-download:latest";
            if (i == 1)
            {
                download.Build = new Build { Context = "../services/download" };
            }
            else
            {
                download.PullPolicy = "never";
            }
            download.Ports.Add($"{8400 + i}:8080");
            download.Restart = "on-failure";
            download.Deploy = new Deploy { Resources = new Resources { Limits = new ResourceSpec { Memory = "768M" } } };
            download.DependsOn["mysql"].Condition = "service_healthy";
        }

        DotnetService(services["gateway"], "youtubesd-gateway",
            "Orchestrador/YoutubeOrchestrador/YoutubeOrchestrador.Gateway/YoutubeOrchestrador.Gateway.csproj",
            "YoutubeOrchestrador.Gateway.dll");
        services["gateway"].Ports.Add("8080:8080");

        var frontend = services["frontend"];
        frontend.Image = "youtubesd-frontend:latest";
        frontend.Build = new Build { Context = "../frontend/youtubesd" };
        frontend.Ports.Add("4200:80");

        Replicas(services, "upload", uploadReplicas, "youtubesd-upload", "services/upload/Dockerfile", 8500, "512M");
        Replicas(services, "media-processor", processorReplicas, "youtubesd-media-processor", "services/upload/media-processor.Dockerfile", 8600, "2G");

        DotnetService(services["identityservice"], "youtubesd-identity",
            "services/identity/identity/identity/identity.csproj", "identity.dll");
        services["identityservice"].DependsOn["mysql"].Condition = "service_healthy";
        DotnetService(services["securityservice"], "youtubesd-security",
            "services/security/src/Security.csproj", "Security.dll");
        services["securityservice"].DependsOn["mysql"].Condition = "service_healthy";

        // Compose service names are lowercase; keep logical .NET discovery names through services__ entries.
        foreach (var name in services.Keys.Where(n => n == "identityservice" || n.StartsWith("upload-")))
            services[name].Environment["Jwt__JwksUri"] = "http://securityservice:8080/api/auth/.well-known/jwks.json";
        foreach (var name in services.Keys.Where(n => n.StartsWith("download-")))
            services[name].Environment["JWT_JWKS_URI"] = "http://securityservice:8080/api/auth/.well-known/jwks.json";

        var kafkaUi = new Service { Name = "kafka-ui", Image = "docker.io/kafbat/kafka-ui:v1.5.0" };
        kafkaUi.Environment["KAFKA_CLUSTERS_0_NAME"] = "kafka";
        kafkaUi.Environment["KAFKA_CLUSTERS_0_BOOTSTRAPSERVERS"] = "kafka:9093";
        kafkaUi.Ports.Add("8090:8080");
        kafkaUi.Networks.Add("aspire");
        kafkaUi.DependsOn["kafka"] = new ServiceDependency { Condition = "service_started" };
        file.AddService(kafkaUi);

        foreach (var service in services.Values)
        {
            SubstituirPlaceholders(service);
        }
    }

    private static void SubstituirPlaceholders(Service service)
    {
        foreach (var chave in service.Environment.Keys.ToList())
        {
            service.Environment[chave] = Substituir(service.Environment[chave]);
        }
        for (var i = 0; i < service.Expose.Count; i++)
        {
            service.Expose[i] = Substituir(service.Expose[i]);
        }
        foreach (var volume in service.Volumes)
        {
            volume.Source = volume.Source is null ? null : Substituir(volume.Source);
        }
    }

    private static string Substituir(string texto)
    {
        foreach (var (placeholder, valor) in Valores)
        {
            texto = texto.Replace(placeholder, valor);
        }
        return texto;
    }

    // Réplicas criadas com AddDockerfile (Upload e media-processor): mesma imagem, construída só na
    // primeira, como o Download.
    private static void Replicas(IDictionary<string, Service> services, string prefix, int count, string image,
        string dockerfile, int basePort, string memory)
    {
        for (var i = 1; i <= count; i++)
        {
            var service = services[$"{prefix}-{i}"];
            service.Image = $"{image}:latest";
            if (i == 1)
            {
                service.Build = new Build { Context = "..", Dockerfile = dockerfile };
            }
            else
            {
                service.Build = null;
                service.PullPolicy = "never";
            }
            service.Ports.Add($"{basePort + i}:8080");
            service.Restart = "on-failure";
            service.Deploy = new Deploy { Resources = new Resources { Limits = new ResourceSpec { Memory = memory } } };
            service.DependsOn["mysql"].Condition = "service_healthy";
        }
    }

    private static void DotnetService(Service service, string image, string project, string assembly)
    {
        service.Image = $"{image}:latest";
        service.Build = new Build
        {
            Context = "..",
            Dockerfile = DotnetDockerfile,
            Args = { ["PROJECT"] = project },
        };
        service.Command = [assembly];
        service.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        service.Restart = "on-failure";
    }

}
