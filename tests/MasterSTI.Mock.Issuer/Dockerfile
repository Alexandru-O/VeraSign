# syntax=docker/dockerfile:1.7
# Build context MUST be the repo root.
# Mock EUDIW Issuer (ADR-0005) — standalone PID issuer + browser wallet simulators.
# No project references — restore is fast.
#
# Wallet-reachable HTTPS: the MAUI wallet refuses cleartext (network_security_config.xml)
# so the issuer must serve TLS on the host-facing port. Dev pfx is mounted by compose;
# the issuer also listens on http:8080 for inter-container use.

# ---- build stage ---------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY tests/MasterSTI.Mock.Issuer/MasterSTI.Mock.Issuer.csproj tests/MasterSTI.Mock.Issuer/

RUN dotnet restore tests/MasterSTI.Mock.Issuer/MasterSTI.Mock.Issuer.csproj

COPY tests/MasterSTI.Mock.Issuer/. tests/MasterSTI.Mock.Issuer/

RUN dotnet publish tests/MasterSTI.Mock.Issuer/MasterSTI.Mock.Issuer.csproj \
        -c Release \
        --no-restore \
        -o /app/publish \
        /p:UseAppHost=false

# ---- runtime stage -------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080 8443

ENTRYPOINT ["dotnet", "MasterSTI.Mock.Issuer.dll"]
