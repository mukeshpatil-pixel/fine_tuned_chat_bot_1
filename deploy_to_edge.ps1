# Edge Deployment Script for Industrial Telemetry Chatbot
# Target: Intel Atom x6211E @ 192.168.0.5
param(
    [string]$TargetHost = "192.168.0.5",
    [string]$User = "wonder",
    [string]$RemoteDir = "~/chatbot"
)

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "🚀 DEPLOYING INDUSTRIAL SLM CHATBOT TO EDGE ($TargetHost)" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Connectivity Check
Write-Host "`n[1/4] Checking ping connectivity to $TargetHost..." -ForegroundColor Yellow
if (-not (Test-Connection -ComputerName $TargetHost -Count 1 -Quiet)) {
    Write-Error "Edge device $TargetHost is unreachable. Check network connection."
    exit 1
}
Write-Host "  -> Device reachable!" -ForegroundColor Green

# 2. Creating remote target directory
Write-Host "`n[2/4] Ensuring remote directory structure ($RemoteDir)..." -ForegroundColor Yellow
ssh ${User}@${TargetHost} "mkdir -p ${RemoteDir}/models"

# 3. Synchronizing project files and model
Write-Host "`n[3/4] Copying container configuration, model weights & source..." -ForegroundColor Yellow
Write-Host "  * Copying models/smollm2-135m-industrial-f16.gguf (270 MB)..."
scp -r models ${User}@${TargetHost}:${RemoteDir}/

Write-Host "  * Copying docker-compose.yml..."
scp docker-compose.yml ${User}@${TargetHost}:${RemoteDir}/

Write-Host "  * Copying backend..."
scp -r ".net backend" ${User}@${TargetHost}:${RemoteDir}/

Write-Host "  * Copying frontend..."
scp -r react-frontend ${User}@${TargetHost}:${RemoteDir}/

Write-Host "  * Copying database seed..."
scp -r iam-seed ${User}@${TargetHost}:${RemoteDir}/

# 4. Triggering remote build & compose
Write-Host "`n[4/4] Launching container stack on edge hardware..." -ForegroundColor Yellow
ssh ${User}@${TargetHost} "cd ${RemoteDir} && docker compose down && docker compose up -d --build"

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host "🎉 DEPLOYMENT COMPLETE!" -ForegroundColor Green
Write-Host "  • Web UI: http://${TargetHost}:5173" -ForegroundColor Green
Write-Host "  • Backend API: http://${TargetHost}:5000" -ForegroundColor Green
Write-Host "  • Ollama (SmolLM2-135M): http://${TargetHost}:11434" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
