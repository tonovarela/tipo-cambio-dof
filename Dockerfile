# syntax=docker/dockerfile:1

# Compila en la arquitectura del host y genera binarios para la de destino (multi-arch con buildx).
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

COPY src/TipoCambioDof/TipoCambioDof.csproj src/TipoCambioDof/
RUN dotnet restore src/TipoCambioDof/TipoCambioDof.csproj -a $TARGETARCH

COPY src/ src/
RUN dotnet publish src/TipoCambioDof/TipoCambioDof.csproj -c Release -a $TARGETARCH --no-restore -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app

ENV TZ=America/Mexico_City

COPY --from=build /app .

# Usuario sin privilegios que ya trae la imagen base.
USER $APP_UID

ENTRYPOINT ["dotnet", "TipoCambioDof.dll"]
CMD ["diario"]

LABEL org.opencontainers.image.title="tipo-cambio-dof" \
      org.opencontainers.image.description="Sincroniza el tipo de cambio FIX USD/MXN (Banxico SF60653) a SQL Server"
