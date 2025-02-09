# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["YnabBotService/YnabBotService.csproj", "YnabBotService/"]
COPY ["Banks/AlfaBank/AlfaBank.csproj", "Banks/AlfaBank/"]
COPY ["Banks/Interfaces/Interfaces.csproj", "Banks/Interfaces/"]
COPY ["Banks/Citibank/Citibank.csproj", "Banks/Citibank/"]
COPY ["Banks/OzonBank/OzonBank.csproj", "Banks/OzonBank/"]
COPY ["Banks/SberBank/SberBank.csproj", "Banks/SberBank/"]
COPY ["Banks/BCC/Bcc.csproj", "Banks/BCC/"]
COPY ["Banks/Tinkoff/Tinkoff.csproj", "Banks/Tinkoff/"]
COPY ["Banks/VTB/VTB.csproj", "Banks/VTB/"]
COPY ["Messengers/Interfaces/Interfaces.csproj", "Messengers/Interfaces/"]
COPY ["Messengers/Telegram/Telegram.csproj", "Messengers/Telegram/"]
COPY ["Persistent/Persistent.csproj", "Persistent/"]
COPY ["Domain/Domain.csproj", "Domain/"]
COPY ["YnabClient/YnabClient.csproj", "YnabClient/"]
RUN dotnet restore "./YnabBotService/YnabBotService.csproj"
COPY . .
WORKDIR "/src/YnabBotService"
RUN dotnet build "./YnabBotService.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./YnabBotService.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "YnabBotService.dll"]