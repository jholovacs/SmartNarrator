# syntax=docker/dockerfile:1
FROM node:20-alpine AS build
WORKDIR /app
COPY package*.json ./
# glob/inflight deprecation warnings come from Angular CLI + Karma transitives; not shipped in the SPA artifact.
RUN npm ci --no-audit --no-fund --loglevel=error
COPY . .
RUN npm run build

FROM nginx:1.27-alpine
COPY nginx/docker.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/client/browser /usr/share/nginx/html
EXPOSE 80
