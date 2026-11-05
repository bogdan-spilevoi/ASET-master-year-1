FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /src

COPY . .
RUN dotnet restore "src/Services/Matching/SmartLost.MatchingService.Api/SmartLost.MatchingService.Api.csproj" --locked-mode

RUN dotnet publish "src/Services/Matching/SmartLost.MatchingService.Api/SmartLost.MatchingService.Api.csproj" --no-restore -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .

RUN useradd -m -u 1001 appuser
USER appuser

ENTRYPOINT ["dotnet", "SmartLost.MatchingService.Api.dll"]
