FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .
RUN dotnet restore "src/AuthService/SmartLost.AuthService.Api/SmartLost.AuthService.Api.csproj" --locked-mode

WORKDIR /src/src/AuthService/SmartLost.AuthService.Api
RUN dotnet publish "SmartLost.AuthService.Api.csproj" --no-restore -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .

RUN useradd -m -u 1001 appuser
USER appuser

ENTRYPOINT ["dotnet", "SmartLost.AuthService.Api.dll"]
