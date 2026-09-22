FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/TinyFlags/ src/TinyFlags/
COPY src/TinyFlags.Http/ src/TinyFlags.Http/
COPY src/TinyFlags.SourceGen/ src/TinyFlags.SourceGen/
COPY samples/MultiServiceHttp/PaymentsService/ samples/MultiServiceHttp/PaymentsService/
RUN dotnet publish samples/MultiServiceHttp/PaymentsService/PaymentsService.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 8082
# The client key is written by the sample's one-shot bootstrap step to a shared volume, not baked
# into the image or passed as a plain env var, so it's read at container start, not build time.
ENTRYPOINT ["/bin/sh", "-c", "export TinyFlags__ApiKey=$(cat /keys/client.key) && exec dotnet PaymentsService.dll"]
