FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /src

COPY . .
RUN dotnet restore "src/Services/Auth/SmartLost.AuthService.Api/SmartLost.AuthService.Api.csproj" --locked-mode

RUN dotnet publish "src/Services/Auth/SmartLost.AuthService.Api/SmartLost.AuthService.Api.csproj" --no-restore -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# USN-8996-1: patch inherited APT packages; use HTTPS while updating APT itself.
RUN sed -i 's|http://|https://|g' /etc/apt/sources.list.d/ubuntu.sources \
    && apt-get update \
    && apt-get install -y --no-install-recommends --only-upgrade apt libapt-pkg6.0t64 \
    && dpkg --compare-versions "$(dpkg-query -W -f='${Version}' apt)" ge 2.8.4ubuntu0.1 \
    && dpkg --compare-versions "$(dpkg-query -W -f='${Version}' libapt-pkg6.0t64)" ge 2.8.4ubuntu0.1 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .

RUN useradd -m -u 1001 appuser
USER appuser

ENTRYPOINT ["dotnet", "SmartLost.AuthService.Api.dll"]
