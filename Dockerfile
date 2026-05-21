FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY AntiSpamBot.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish AntiSpamBot.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
RUN useradd --create-home --shell /usr/sbin/nologin appuser
COPY --from=build /app/publish ./
RUN chown -R appuser:appuser /app
USER appuser
VOLUME ["/app/data"]
ENTRYPOINT ["dotnet", "AntiSpamBot.dll"]
