FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY WebVibeTest/WebVibeTest.csproj WebVibeTest/
RUN dotnet restore WebVibeTest/WebVibeTest.csproj

COPY WebVibeTest/ WebVibeTest/
RUN dotnet publish WebVibeTest/WebVibeTest.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0 \
    PersistentStorage__Path=/var/data \
    PORT=10000

RUN mkdir -p /var/data && chown -R app:app /var/data
COPY --from=build --chown=app:app /app/publish .

USER app
EXPOSE 10000
ENTRYPOINT ["dotnet", "WebVibeTest.dll"]
