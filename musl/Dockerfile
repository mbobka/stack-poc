FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /app
COPY . .
RUN dotnet publish -c Release -r linux-musl-x64 --self-contained true -o out

FROM alpine:3.19
RUN apk add --no-cache libstdc++ libgcc
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

WORKDIR /app
COPY --from=build /app/out .

ENTRYPOINT ["./stack-poc"]
