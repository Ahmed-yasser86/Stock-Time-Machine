# Frontend (Vite build + nginx serve).
# Build from the repo root:
#   docker build --build-arg VITE_API_URL=http://localhost:8080 -f frontend/Dockerfile .
# NOTE: Vite bakes VITE_API_URL into the bundle at build time. Rebuild the
# image (or override the build arg) when the API base URL changes.
FROM node:22-alpine AS build
WORKDIR /app
ARG VITE_API_URL=http://localhost:8080
ENV VITE_API_URL=$VITE_API_URL
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM nginx:alpine AS final
COPY frontend/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
  CMD wget -q -O /dev/null http://127.0.0.1/ || exit 1
