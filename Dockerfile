FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Dispatcher.Core/Dispatcher.Core.csproj Dispatcher.Core/
COPY Dispatcher.Cli/Dispatcher.Cli.csproj Dispatcher.Cli/
RUN dotnet restore Dispatcher.Cli/Dispatcher.Cli.csproj
COPY Dispatcher.Core/ Dispatcher.Core/
COPY Dispatcher.Cli/ Dispatcher.Cli/
RUN dotnet publish Dispatcher.Cli/Dispatcher.Cli.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app/ ./
ENTRYPOINT ["dotnet", "Dispatcher.Cli.dll"]
