# syntax=docker/dockerfile:1
#
# Dockerfile multi-stage parametrizado por servicio (Story 1.1).
# Verificado en runtime: las 4 imágenes construyen y los servicios alcanzan (healthy).
# USER no root + HEALTHCHECK + tag específico.
#
ARG DOTNET_TAG=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_TAG} AS build
WORKDIR /src
COPY . .
ARG PROJECT_PATH
RUN dotnet restore "${PROJECT_PATH}"
RUN dotnet publish "${PROJECT_PATH}" -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_TAG} AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && useradd --create-home --shell /usr/sbin/nologin appuser
WORKDIR /app
COPY --from=build /app .
USER appuser
ARG APP_DLL
ENV APP_DLL=${APP_DLL}
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=3s --start-period=25s --retries=10 \
    CMD curl -fsS http://localhost:8080/health || exit 1
ENTRYPOINT ["sh", "-c", "dotnet \"$APP_DLL\""]
