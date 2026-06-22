# mastersti/pixelcommit-cli -- ADR-0008 determinism spike image.
#
# Build:   docker build -t mastersti/pixelcommit-cli -f tools/RenderCommit.Cli/Dockerfile .
# Render:  docker run --rm -v "$PWD:/work" mastersti/pixelcommit-cli render /work/sample.pdf
# Harness: docker run --rm -v "$PWD:/work" mastersti/pixelcommit-cli /work/tools/RenderCommit.Cli/spike-harness.sh
#
# Image carries everything the spike needs at runtime: pinned libpdfium.so,
# fonts-noto-core for the diacritics fixture, jq for the harness. The native
# binary is COPYed from the host's tools/pdfium-v1/linux-x64/ tree so the
# image inherits whatever sha256 the operator pinned in RenderProfiles.cs.

ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

COPY tools/RenderCommit.Cli/RenderCommit.Cli.csproj tools/RenderCommit.Cli/
RUN dotnet restore tools/RenderCommit.Cli/RenderCommit.Cli.csproj

COPY tools/RenderCommit.Cli/ tools/RenderCommit.Cli/
# Algorithm files are source-linked from MasterSTI.Api (single source of
# truth shared with the production endpoint). CLI csproj uses relative
# Compile Include="..\..\src\..." so we need this subtree present at build.
COPY src/MasterSTI.Api/Common/Rendering/ src/MasterSTI.Api/Common/Rendering/

RUN dotnet publish tools/RenderCommit.Cli/RenderCommit.Cli.csproj \
        -c Release \
        -r linux-x64 \
        --self-contained false \
        -o /app

FROM mcr.microsoft.com/dotnet/runtime:${DOTNET_VERSION} AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends fonts-noto-core jq ca-certificates \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app /app
COPY tools/pdfium-v1/ /app/pdfium-v1/
COPY tools/RenderCommit.Cli/spike-harness.sh /usr/local/bin/spike-harness.sh
RUN chmod +x /usr/local/bin/spike-harness.sh

ENV PDFIUM_ROOT=/app/pdfium-v1
ENV NOTO_TTF_PATH=/usr/share/fonts/truetype/noto/NotoSans-Regular.ttf

ENTRYPOINT ["dotnet", "/app/render-commit.dll"]
