FROM mcr.microsoft.com/dotnet/sdk:7.0-bullseye-slim AS build-env
WORKDIR /app

COPY Rebus.GoogleCloudPubSub/*.csproj ./Rebus.GoogleCloudPubSub/
COPY Rebus.GoogleCloudPubSub.Tests/*.csproj ./Rebus.GoogleCloudPubSub.Tests/

COPY Rebus.GoogleCloudPubSub.sln .
RUN dotnet restore

COPY . ./
FROM build-env AS test