# syntax=docker/dockerfile:1.7
# Build context MUST be the repo root so cross-project references (MasterSTI.Shared) resolve.
#
# Optional HTTPS:
#   To enable HTTPS inside the container, mount your dev cert and set:
#     volumes: [ "${USERPROFILE}/.aspnet/https:/https:ro" ]
#     env:    [ "ASPNETCORE_Kestrel__Certificates__Default__Path=/https/aspnetapp.pfx",
#               "ASPNETCORE_Kestrel__Certificates__Default__Password=<pwd>",
#               "ASPNETCORE_URLS=https://+:8443;http://+:8080" ]
# For the demo we run plain HTTP on 8080 — Blazor Server SignalR works over HTTP.

# ---- build stage ---------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only csproj files first to maximize layer caching on subsequent rebuilds.
COPY src/MasterSTI.Api/MasterSTI.Api.csproj      src/MasterSTI.Api/
COPY src/MasterSTI.Shared/MasterSTI.Shared.csproj src/MasterSTI.Shared/

RUN dotnet restore src/MasterSTI.Api/MasterSTI.Api.csproj

# Copy the rest of the source for the API + its referenced shared lib.
COPY src/MasterSTI.Api/.    src/MasterSTI.Api/
COPY src/MasterSTI.Shared/. src/MasterSTI.Shared/

RUN dotnet publish src/MasterSTI.Api/MasterSTI.Api.csproj \
        -c Release \
        --no-restore \
        -o /app/publish \
        /p:UseAppHost=false

# ---- runtime stage -------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl is used by the compose healthcheck.
# fonts-dejavu-core gives the template PDF renderer a Unicode-capable TTF so
# Romanian diacritics (ăâîșțĂÂÎȘȚ) survive — see Common/Templates/UnicodeFonts.cs.
# fonts-noto-core gives PDFium the Noto fallback set the Pixel-Bound QES
# spike harness validated for the render-commitment endpoint (ADR-0008).
RUN apt-get update && apt-get install -y --no-install-recommends \
        curl fonts-dejavu-core fonts-noto-core \
    && rm -rf /var/lib/apt/lists/*

# storage/* is a runtime mount point (api-storage volume) for uploads/prepared/signed/templates.
RUN mkdir -p /app/storage/uploads /app/storage/prepared /app/storage/signed /app/storage/templates

COPY --from=build /app/publish ./

# Pinned PDFium binary for the Render Commitment endpoint. Same provenance
# as the spike CLI image; sha256 enforced by RenderProfiles registry.
COPY tools/pdfium-v1/ /app/pdfium-v1/

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    RenderCommitment__PdfiumRoot=/app/pdfium-v1

EXPOSE 8080

ENTRYPOINT ["dotnet", "MasterSTI.Api.dll"]
