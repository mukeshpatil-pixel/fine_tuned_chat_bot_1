FROM node:20-alpine
WORKDIR /app
COPY package.json ./
RUN npm install --omit=dev
COPY seed.js ./
CMD ["node", "seed.js"]