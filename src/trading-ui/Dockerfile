# Build stage
FROM node:20-alpine AS build
WORKDIR /app

COPY package*.json ./
RUN npm ci --legacy-peer-deps

COPY . .
ARG BUILD_CONFIG=production
RUN npm run build -- --configuration ${BUILD_CONFIG}

# Production stage
FROM nginx:alpine
COPY --from=build /app/dist/trading-ui /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
