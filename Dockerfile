# ---------- build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY *.csproj ./
RUN dotnet restore

COPY . ./
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# ---------- runtime stage ----------
FROM mcr.microsoft.com/dotnet/runtime:9.0

RUN apt-get update \
    && apt-get install -y --no-install-recommends tzdata locales iputils-ping \
    && rm -rf /var/lib/apt/lists/* \
    && sed -i 's/# ru_RU.UTF-8 UTF-8/ru_RU.UTF-8 UTF-8/' /etc/locale.gen \
    && locale-gen

ENV TZ=Europe/Moscow \
    LANG=ru_RU.UTF-8 \
    LC_ALL=ru_RU.UTF-8 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

VOLUME ["/data"]

WORKDIR /app

COPY --from=build /app/publish ./

COPY run.sh ./run.sh
RUN chmod +x ./run.sh

ENTRYPOINT ["./run.sh"]
