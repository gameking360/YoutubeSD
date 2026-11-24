FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN --mount=type=secret,id=build_ca,target=/usr/local/share/ca-certificates/build-ca.crt \
    if [ -f /usr/local/share/ca-certificates/build-ca.crt ]; then update-ca-certificates; fi \
    && dotnet publish services/security/src/Security.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "Security.dll"]
