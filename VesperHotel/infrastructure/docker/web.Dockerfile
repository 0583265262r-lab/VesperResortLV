FROM node:22.23.3-alpine AS build
WORKDIR /source
RUN corepack enable
COPY frontend/hotel-web/package.json frontend/hotel-web/pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile
COPY frontend/hotel-web/ ./
RUN pnpm build

FROM nginx:1.30.5-alpine AS runtime
COPY infrastructure/nginx/default.conf /etc/nginx/conf.d/default.conf
COPY --from=build /source/dist/ /usr/share/nginx/html/
EXPOSE 8080

