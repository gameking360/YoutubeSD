FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["services/identity/identity/identity/identity.csproj", "services/identity/identity/identity/"]
COPY ["Orchestrador/YoutubeOrchestrador/YoutubeOrchestrador.ServiceDefaults/YoutubeOrchestrador.ServiceDefaults.csproj", "Orchestrador/YoutubeOrchestrador/YoutubeOrchestrador.ServiceDefaults/"]
RUN dotnet restore "services/identity/identity/identity/identity.csproj"

COPY . .
RUN dotnet publish "services/identity/identity/identity/identity.csproj" -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "identity.dll"]
