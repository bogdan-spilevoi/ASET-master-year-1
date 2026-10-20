FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /src
COPY . .
RUN dotnet restore "src/Services/Auth/SmartLost.AuthService.Worker/SmartLost.AuthService.Worker.csproj" --locked-mode
RUN dotnet publish "src/Services/Auth/SmartLost.AuthService.Worker/SmartLost.AuthService.Worker.csproj" --no-restore -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
RUN useradd -m -u 1001 appuser
USER appuser
ENTRYPOINT ["dotnet", "SmartLost.AuthService.Worker.dll"]
