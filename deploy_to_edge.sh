#!/usr/bin/env bash
# Edge Deployment Script for Industrial Telemetry Chatbot
# Target: Intel Atom x6211E @ 192.168.0.5

TARGET_HOST="${1:-192.168.0.5}"
REMOTE_USER="${2:-wonder}"
REMOTE_DIR="${3:-~/chatbot}"

set -e

echo "=========================================================="
echo "🚀 DEPLOYING INDUSTRIAL SLM CHATBOT TO EDGE ($TARGET_HOST)"
echo "=========================================================="

echo -e "\n[1/4] Checking connectivity to $TARGET_HOST..."
ping -c 1 "$TARGET_HOST" > /dev/null || { echo "Edge device $TARGET_HOST is unreachable."; exit 1; }
echo "  -> Device reachable!"

echo -e "\n[2/4] Ensuring remote directory structure..."
ssh "${REMOTE_USER}@${TARGET_HOST}" "mkdir -p ${REMOTE_DIR}/models"

echo -e "\n[3/4] Transferring model weights, compose spec, and services..."
scp -r models "${REMOTE_USER}@${TARGET_HOST}:${REMOTE_DIR}/"
scp docker-compose.yml "${REMOTE_USER}@${TARGET_HOST}:${REMOTE_DIR}/"
scp -r ".net backend" "${REMOTE_USER}@${TARGET_HOST}:${REMOTE_DIR}/"
scp -r react-frontend "${REMOTE_USER}@${TARGET_HOST}:${REMOTE_DIR}/"
scp -r iam-seed "${REMOTE_USER}@${TARGET_HOST}:${REMOTE_DIR}/"

echo -e "\n[4/4] Launching Docker stack on edge device..."
ssh "${REMOTE_USER}@${TARGET_HOST}" "cd ${REMOTE_DIR} && docker compose down && docker compose up -d --build"

echo "=========================================================="
echo "🎉 DEPLOYMENT COMPLETE!"
echo "  • Web UI: http://${TARGET_HOST}:5173"
echo "  • Backend API: http://${TARGET_HOST}:5000"
echo "  • Ollama (SmolLM2-135M): http://${TARGET_HOST}:11434"
echo "=========================================================="
