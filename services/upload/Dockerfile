# Upload API. Contexto de build: raiz do repositório.
#   docker build -f services/upload/Dockerfile -t youtubesd-upload .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Orchestrador/YoutubeOrchestrador/YoutubeOrchestrador.ServiceDefaults/YoutubeOrchestrador.ServiceDefaults.csproj Orchestrador/YoutubeOrchestrador/YoutubeOrchestrador.ServiceDefaults/
COPY services/upload/upload/upload.messaging/upload.messaging.csproj services/upload/upload/upload.messaging/
COPY services/upload/upload/upload/upload.csproj services/upload/upload/upload/
RUN --mount=type=secret,id=build_ca,target=/usr/local/share/ca-certificates/build-ca.crt \
    if [ -f /usr/local/share/ca-certificates/build-ca.crt ]; then update-ca-certificates; fi \
    && dotnet restore services/upload/upload/upload/upload.csproj

COPY Orchestrador/YoutubeOrchestrador/YoutubeOrchestrador.ServiceDefaults/ Orchestrador/YoutubeOrchestrador/YoutubeOrchestrador.ServiceDefaults/
COPY services/upload/upload/upload.messaging/ services/upload/upload/upload.messaging/
COPY services/upload/upload/upload/ services/upload/upload/upload/
RUN dotnet publish services/upload/upload/upload/upload.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "upload.dll"]
