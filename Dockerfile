# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src
COPY InventoryApi.sln ./
COPY src/InventoryApi/InventoryApi.csproj src/InventoryApi/
RUN dotnet restore src/InventoryApi/InventoryApi.csproj
COPY src/ src/
RUN dotnet publish src/InventoryApi/InventoryApi.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
WORKDIR /app
RUN adduser -D -u 1001 appuser
COPY --from=build /app ./
USER appuser
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD wget -qO- http://localhost:8080/healthz || exit 1
ENTRYPOINT ["dotnet", "InventoryApi.dll"]
