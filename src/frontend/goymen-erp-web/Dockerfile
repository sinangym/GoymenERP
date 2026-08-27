FROM node:24-alpine AS build
WORKDIR /app

COPY src/frontend/goymen-erp-web/package*.json ./
RUN npm ci
COPY src/frontend/goymen-erp-web/ ./

ARG VITE_BOOTSTRAP_IDENTITY_ENABLED=false
ARG VITE_DEV_USER=company-a-user
ENV VITE_BOOTSTRAP_IDENTITY_ENABLED=${VITE_BOOTSTRAP_IDENTITY_ENABLED}
ENV VITE_DEV_USER=${VITE_DEV_USER}
RUN npm run build

FROM nginx:1.28-alpine AS runtime
COPY src/frontend/goymen-erp-web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
