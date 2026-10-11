FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY . .
RUN dotnet publish src/Hotel.Worker/Hotel.Worker.csproj -c Release -o /publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends tzdata \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/data/notifications \
    && chown -R app:app /app
WORKDIR /app
COPY --from=build --chown=app:app /publish/ ./
USER app
ENTRYPOINT ["dotnet", "Hotel.Worker.dll"]
