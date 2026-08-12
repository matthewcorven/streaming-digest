FROM node:22-bookworm-slim

RUN apt-get update && apt-get install -y wget && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build
RUN npx playwright install --with-deps chromium

EXPOSE 3000
CMD ["npm", "start"]
