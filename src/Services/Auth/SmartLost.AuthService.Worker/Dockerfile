FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /src
COPY . .
RUN dotnet restore "src/Services/Auth/SmartLost.AuthService.Worker/SmartLost.AuthService.Worker.csproj" --locked-mode
RUN dotnet publish "src/Services/Auth/SmartLost.AuthService.Worker/SmartLost.AuthService.Worker.csproj" --no-restore -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get upgrade -y --no-install-recommends \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
RUN apt-get update && DEBIAN_FRONTEND=noninteractive apt-get upgrade -y \
    && rm -rf /var/lib/apt/lists/* \
    && useradd -m -u 1001 appuser
USER appuser
ENTRYPOINT ["dotnet", "SmartLost.AuthService.Worker.dll"]
