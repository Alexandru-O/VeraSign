# syntax=docker/dockerfile:1.7
# Build context MUST be the repo root so cross-project references (MasterSTI.Shared) resolve.
# Blazor Server: SignalR/WebSocket works over plain HTTP — no buffering proxy in front.

# ---- build stage ---------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/MasterSTI.Web/.    src/MasterSTI.Web/
COPY src/MasterSTI.Shared/. src/MasterSTI.Shared/

RUN dotnet publish src/MasterSTI.Web/MasterSTI.Web.csproj \
        -c Release \
        -o /app/publish \
        -p:UseAppHost=false

# ---- runtime stage -------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080

ENTRYPOINT ["dotnet", "MasterSTI.Web.dll"]
