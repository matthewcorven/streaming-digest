FROM node:22-bookworm-slim

WORKDIR /app

COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build
RUN npx playwright install --with-deps chromium

EXPOSE 3000
CMD ["npm", "start"]
