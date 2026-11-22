# YoutubeSD

Plataforma de vídeos sob demanda com arquitetura de microsserviços.
Trabalho final de Sistemas Distribuídos — FURB.

## Estrutura

```
Orchestrador/YoutubeOrchestrador/
  YoutubeOrchestrador.AppHost/          orquestração com .NET Aspire
  YoutubeOrchestrador.Gateway/          gateway YARP (porta 8080)
  YoutubeOrchestrador.ServiceDefaults/  health check e telemetria dos serviços .NET
services/
  download/   reprodução dos vídeos (Java, Spring Boot)
  upload/     envio e processamento dos vídeos (C#)
contracts/    contratos das mensagens (JSON Schema)
infra/
  kafka/      criação dos tópicos
  ministack/  criação dos buckets do S3
  seed/       vídeo de exemplo
  falhas/     roteiro dos cenários de falha
deploy/       Docker Compose gerado a partir do AppHost
```

## Pré-requisitos

- Docker Desktop aberto.
- .NET SDK 10, para subir pelo Aspire.
- Java 25, apenas para rodar os testes do Download fora do Docker.
- Git Bash, para os scripts de `infra/`.

## Subir pelo Aspire

```
cd Orchestrador/YoutubeOrchestrador/YoutubeOrchestrador.AppHost
dotnet run --launch-profile http
```

O dashboard abre em http://localhost:15006. O número de réplicas do Download é configurável:

```
dotnet run --launch-profile http -- --DownloadReplicas 1
```

## Subir pelo Docker Compose

```
cd deploy
docker compose up --build -d
```

Para parar: `docker compose down`. Os dados ficam nos volumes.
O dashboard do Aspire fica em http://localhost:18888; o token de acesso aparece em `docker compose logs compose-dashboard`.

Para regenerar o `docker-compose.yaml` depois de alterar o AppHost, na pasta do AppHost:

```
dotnet dnx --yes aspire.cli@13.6.5 -- publish -o ../../../deploy --non-interactive
```

## Um ambiente de cada vez

Aspire e Compose usam as mesmas portas. Antes de trocar:

- do Compose para o Aspire: `docker compose down` em `deploy/`;
- do Aspire para o Compose: pare o AppHost e os containers persistentes dele (`redis`, `kafka`, `kafka-ui`, `mysql` e `ministack`).

## Endereços

- Gateway: http://localhost:8080
- Download (réplicas): http://localhost:8401, 8402 e 8403
- Swagger do Download: http://localhost:8401/swagger-ui/index.html
- Kafka UI: http://localhost:8090

## Vídeo de exemplo

Com o ambiente no ar, na raiz do repositório:

```
./infra/seed/seed-video.sh demo-1 "Video de demonstracao"
./infra/seed/seed-video.sh meu-video "Meu video" "C:/caminho/para/arquivo.mp4"
```

O script gera o HLS (360p e 720p) com FFmpeg num container, envia ao S3 e publica o `VideoPublished`.

## Cenários de falha

Com o ambiente no ar e as 3 réplicas do Download, na raiz do repositório:

```
./infra/falhas/f2-kill-replica.sh
./infra/falhas/f3-duplicada.sh
./infra/falhas/f4-venenosa.sh
./infra/falhas/f6-kafka-fora.sh
./infra/falhas/f8-replicas.sh
./infra/falhas/limpar.sh
```

Cada script provoca a falha, mostra o estado antes e depois e termina com `RESULTADO: OK` ou `FALHOU`.
O `limpar.sh` remove os dados de teste do catálogo.

## Testes do Download

Com o Docker aberto, em `services/download`:

```
./mvnw verify
```
