# syntax=docker/dockerfile:1.7
# Build context MUST be the repo root.
# Mock QTSP has no project references — restore is fast.

# ---- build stage ---------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY tests/MasterSTI.Mock.Qtsp/MasterSTI.Mock.Qtsp.csproj tests/MasterSTI.Mock.Qtsp/

RUN dotnet restore tests/MasterSTI.Mock.Qtsp/MasterSTI.Mock.Qtsp.csproj

COPY tests/MasterSTI.Mock.Qtsp/. tests/MasterSTI.Mock.Qtsp/

RUN dotnet publish tests/MasterSTI.Mock.Qtsp/MasterSTI.Mock.Qtsp.csproj \
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

EXPOSE 8080

ENTRYPOINT ["dotnet", "MasterSTI.Mock.Qtsp.dll"]
