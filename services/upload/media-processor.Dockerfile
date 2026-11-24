# media-processor (worker FFmpeg). Contexto de build: raiz do repositório.
#   docker build -f services/upload/media-processor.Dockerfile -t youtubesd-media-processor .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Orchestrador/YoutubeOrchestrador/YoutubeOrchestrador.ServiceDefaults/YoutubeOrchestrador.ServiceDefaults.csproj Orchestrador/YoutubeOrchestrador/YoutubeOrchestrador.ServiceDefaults/
COPY services/upload/upload/upload.messaging/upload.messaging.csproj services/upload/upload/upload.messaging/
COPY services/upload/upload/media-processor/media-processor.csproj services/upload/upload/media-processor/
RUN --mount=type=secret,id=build_ca,target=/usr/local/share/ca-certificates/build-ca.crt \
    if [ -f /usr/local/share/ca-certificates/build-ca.crt ]; then update-ca-certificates; fi \
    && dotnet restore services/upload/upload/media-processor/media-processor.csproj

COPY Orchestrador/YoutubeOrchestrador/YoutubeOrchestrador.ServiceDefaults/ Orchestrador/YoutubeOrchestrador/YoutubeOrchestrador.ServiceDefaults/
COPY services/upload/upload/upload.messaging/ services/upload/upload/upload.messaging/
COPY services/upload/upload/media-processor/ services/upload/upload/media-processor/
RUN dotnet publish services/upload/upload/media-processor/media-processor.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Processor__WorkDirectory=/tmp/media
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "media-processor.dll"]
