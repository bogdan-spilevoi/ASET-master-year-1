FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /src

COPY . .
RUN dotnet restore "src/Services/Matching/SmartLost.MatchingService.Api/SmartLost.MatchingService.Api.csproj" --locked-mode

RUN dotnet publish "src/Services/Matching/SmartLost.MatchingService.Api/SmartLost.MatchingService.Api.csproj" --no-restore -c Release -o /app/publish /p:UseAppHost=false

# Opt-in only (--target migrations); the default build stops at the runtime stage below.
FROM build AS migrations-build
RUN dotnet tool restore
RUN dotnet ef migrations bundle --no-build --configuration Release \
    --project src/Services/Matching/SmartLost.MatchingService.Infrastructure \
    --startup-project src/Services/Matching/SmartLost.MatchingService.Infrastructure \
    --context MatchingDbContext --output /app/migrations/efbundle

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime-base
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

RUN apt-get update \
    && apt-get upgrade -y --no-install-recommends \
    && rm -rf /var/lib/apt/lists/*

RUN useradd -m -u 1001 appuser

FROM runtime-base AS migrations
COPY --from=migrations-build /app/migrations/efbundle .
USER appuser
ENTRYPOINT ["./efbundle"]

FROM runtime-base AS runtime
COPY --from=build /app/publish .
USER appuser

ENTRYPOINT ["dotnet", "SmartLost.MatchingService.Api.dll"]
