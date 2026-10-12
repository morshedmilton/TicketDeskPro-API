# Multi-stage build: SDK for compile, slim aspnet runtime for the image.
# Render runs this with the Docker runtime and routes traffic to port 8080.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/TicketDeskPro.Api/TicketDeskPro.Api.csproj -c Release -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /out .

# Serilog writes logs/ and the file storage writes uploads/ under the working dir —
# the runtime image runs as the non-root "app" user, so it needs ownership.
RUN mkdir -p /app/logs /app/uploads && chown -R $APP_UID /app

# Render's internal health check and default traffic port are 10000 (PORT default),
# so the app binds there — anything else times out the deploy.
ENV ASPNETCORE_URLS=http://0.0.0.0:10000
EXPOSE 10000
USER $APP_UID

ENTRYPOINT ["dotnet", "TicketDeskPro.Api.dll"]
