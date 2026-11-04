FROM mcr.microsoft.com/dotnet/sdk:10.0.401@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317 AS build
WORKDIR /src

COPY . .
RUN dotnet restore "src/Services/Listings/SmartLost.ListingsService.Api/SmartLost.ListingsService.Api.csproj" --locked-mode
RUN dotnet publish "src/Services/Listings/SmartLost.ListingsService.Api/SmartLost.ListingsService.Api.csproj" --no-restore --configuration Release --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12@sha256:222759b391a1aaf241166672c8f99b2d4ada452e7b5319f3c6e8f265a37b5ad4 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
USER app

ENTRYPOINT ["dotnet", "SmartLost.ListingsService.Api.dll"]
