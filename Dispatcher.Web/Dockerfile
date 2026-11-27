FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Dispatcher.Core/Dispatcher.Core.csproj Dispatcher.Core/
COPY Dispatcher.Web/Dispatcher.Web.csproj Dispatcher.Web/
RUN dotnet restore Dispatcher.Web/Dispatcher.Web.csproj
COPY Dispatcher.Core/ Dispatcher.Core/
COPY Dispatcher.Web/ Dispatcher.Web/
RUN dotnet publish Dispatcher.Web/Dispatcher.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/ ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Dispatcher.Web.dll"]
