#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:5.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:5.0-buster-slim AS build
WORKDIR /src
COPY ["YnabBotService/YnabBotService.csproj", "YnabBotService/"]
COPY ["Banks/AlfaBank/AlfaBank.csproj", "Banks/AlfaBank/"]
COPY ["Banks/Interfaces/Interfaces.csproj", "Banks/Interfaces/"]
COPY ["Messengers/Telegram/Telegram.csproj", "Messengers/Telegram/"]
COPY ["Messengers/Interfaces/Interfaces.csproj", "Messengers/Interfaces/"]
COPY ["Banks/SberBank/SberBank.csproj", "Banks/SberBank/"]
COPY ["YnabClient/YnabClient.csproj", "YnabClient/"]
COPY ["Persistent/Persistent.csproj", "Persistent/"]
COPY ["Domain/Domain.csproj", "Domain/"]
COPY ["Banks/VTB/VTB.csproj", "Banks/VTB/"]
COPY ["Banks/Citibank/Citibank.csproj", "Banks/Citibank/"]
COPY ["Banks/Tinkoff/Tinkoff.csproj", "Banks/Tinkoff/"]
RUN dotnet restore "YnabBotService/YnabBotService.csproj"
COPY . .
WORKDIR "/src/YnabBotService"
RUN dotnet build "YnabBotService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "YnabBotService.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "YnabBotService.dll"]