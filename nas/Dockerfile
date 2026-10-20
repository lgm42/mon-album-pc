# Mon Album PC — NAS server (TrueNAS SCALE Custom App, docker compose).
# Build from the repository root:  docker build -f nas/Dockerfile .
# The Android APK is expected in nas/android/ (monalbumpc.apk + monalbumpc.apk.json), copied by CI.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY windows/Directory.Build.props windows/
COPY windows/src/MonAlbumPc.Core/ windows/src/MonAlbumPc.Core/
COPY windows/src/MonAlbumPc.Server/ windows/src/MonAlbumPc.Server/
COPY nas/ nas/
RUN dotnet publish nas/src/MonAlbumPc.Nas -c Release -r linux-x64 --self-contained false -o /app \
    && mkdir -p /app/android \
    && if [ -f nas/android/monalbumpc.apk ]; then cp nas/android/monalbumpc.apk nas/android/monalbumpc.apk.json /app/android/; fi

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# ffmpeg: video thumbnails in the web viewer. libgomp1: ImageMagick's OpenMP runtime.
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg libgomp1 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .

ENV MONALBUMPC_CONFIG=/config \
    MONALBUMPC_PHOTOS=/photos \
    MONALBUMPC_ADMIN_PORT=48622 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
VOLUME ["/config", "/photos"]
# Phone API (HTTPS), download page (HTTP), web administration (HTTPS). Run with host networking for mDNS.
EXPOSE 48620 48621 48622
# TrueNAS "apps" user by default; override with `user:` in the compose file.
USER 568:568
ENTRYPOINT ["dotnet", "MonAlbumPc.Nas.dll"]
