# syntax=docker/dockerfile:1.7
# One image, one program, several roles (master plan 12.3, ADR 002).
# The SDK stage runs on the build machine's architecture and cross-compiles for the target, so the
# arm64 image needs no emulation (master plan 13.7).

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
ARG TARGETARCH
ARG VERSION=0.0.0
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/ src/
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore src/Hosts/Ardea.Host/Ardea.Host.csproj -a "$TARGETARCH"
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish src/Hosts/Ardea.Host/Ardea.Host.csproj -c Release -a "$TARGETARCH" --no-restore \
      -p:Version="$VERSION" -p:UseAppHost=true -o /app \
 && mkdir -p /scratch

# Without shell and package manager, non-root, with time zone data and ICU (experiment 0.7, ADR 0003).
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra
WORKDIR /app
COPY --from=build /app .
# Master data that migrate loads after the migrations (master plan 4.11); ARDEA_DATA_DIR overrides the path.
COPY data/ /app/data/
# Working space for large archives. A named volume mounted here takes over this directory's owner,
# so the non-root user can write to it although the root file system is read-only.
COPY --from=build --chown=$APP_UID:$APP_UID /scratch /scratch
ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=0
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["/app/ardea"]
CMD ["api"]
