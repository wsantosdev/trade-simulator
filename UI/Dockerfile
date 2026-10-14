FROM node:24-alpine AS build
WORKDIR /src
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npx ng build main && npx ng build exchange

FROM nginx:alpine AS final
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY hub.conf /etc/nginx/hub.conf
COPY --from=build /src/dist/main/browser /usr/share/nginx/html/main
COPY --from=build /src/dist/exchange/browser /usr/share/nginx/html/exchange
EXPOSE 4200 4204
