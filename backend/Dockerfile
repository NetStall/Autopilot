# syntax=docker/dockerfile:1
# Build context: repository root (for .editorconfig, which holds the analyzer rules), see Dockerfile.dockerignore.

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0.402-noble AS build
WORKDIR /src

# Restore first so the layer is cached as long as no project file changes.
COPY .editorconfig ./
COPY backend/Directory.Build.props backend/JobAutopilot.sln backend/
COPY backend/src/JobAutopilot.Core/JobAutopilot.Core.csproj backend/src/JobAutopilot.Core/
COPY backend/src/JobAutopilot.Infrastructure/JobAutopilot.Infrastructure.csproj backend/src/JobAutopilot.Infrastructure/
COPY backend/src/JobAutopilot.Api/JobAutopilot.Api.csproj backend/src/JobAutopilot.Api/
RUN dotnet restore backend/src/JobAutopilot.Api/JobAutopilot.Api.csproj

COPY backend/src/ backend/src/
RUN dotnet publish backend/src/JobAutopilot.Api/JobAutopilot.Api.csproj \
    --configuration Release --no-restore --output /app /p:UseAppHost=false

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-noble AS runtime
WORKDIR /app
COPY --from=build /app ./

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080

# Non-root user that ships with the official image (UID 1654).
USER $APP_UID

# No curl in the image: bash's /dev/tcp is enough for a local health probe.
HEALTHCHECK --interval=10s --timeout=5s --start-period=20s --retries=5 \
    CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health HTTP/1.0\\r\\nHost: localhost\\r\\n\\r\\n' >&3 && head -n 1 <&3 | grep -q ' 200 '"]

ENTRYPOINT ["dotnet", "JobAutopilot.Api.dll"]
