# HEX Revival master server. Build from this folder:  docker build -t hexrevival-server .
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY nuget.config .
COPY HexRevival.Server/ HexRevival.Server/
RUN dotnet publish HexRevival.Server -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app .
# Accounts live in a volume so they survive container updates.
ENV HEX_DATA=/data HEX_PORT=7777
RUN mkdir /data && chown app /data
VOLUME /data
EXPOSE 7777
# The image's non-root user.
USER app
ENTRYPOINT ["dotnet", "HexRevival.Server.dll"]
