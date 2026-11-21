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
        ["${KAFKA_INIT_BINDMOUNT_0}"] = "../infra/kafka",
        ["${MINISTACK_BINDMOUNT_0}"] = "../infra/ministack/ready.d",
    };

    public static void Arquivo(ComposeFile file, int downloadReplicas)
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

        DotnetService(services["uploadservice"], "youtubesd-upload",
            "services/upload/upload/upload/upload.csproj",
            "upload.dll");

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
