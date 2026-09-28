# Industrial Sensor Telemetry & AI Report Platform

A production-grade, distributed industrial telemetry analytics and automated PDF reporting platform. Features a **100% local, free LLM conversational assistant** powered by Ollama (`qwen2.5:1.5b`), real-time SignalR WebSockets, a RabbitMQ background job queue, and high-performance QuestPDF report generation.

---

## 🌟 Key Features

- **100% Local & Free AI Chatbot**:
  - Pure LLM-driven intent extraction using **Qwen 2.5 (1.5B)** via Ollama.
  - Zero external API costs, zero cloud dependencies, and 100% data privacy.
  - Multi-turn conversational memory with automatic parameter carry-forward.
  - Off-topic guardrails and intelligent history sanitization.
- **Interactive UI Shelves & Chips**:
  - Dynamic machine selection chips (`Boiler Feed Pump Motor`, `Air Compressor Motor`, etc.).
  - Rapid timeframe presets (`24h`, `5d`, `14d`, `30d`), custom calendar date-range picker, and arbitrary natural language timeframes (`for last 3 days`, `2 weeks`).
  - Interactive confirmation actions (`Yes, Queue PDF Report` / `Change Options`).
- **Asynchronous Background PDF Generation**:
  - Message broker queueing via **RabbitMQ** with progress tracking and real-time status updates.
  - Multi-page industrial PDF reports generated using **QuestPDF** with embedded vector charts, metrics tables, and event excursion logs.
  - Dual report modes: **Full Raw Data Table** (unaggregated rows) vs. **Graphs & Trends Only**.
- **Time-Series Telemetry Engine**:
  - **TimescaleDB** (PostgreSQL 16) with automated database seeding of realistic industrial sensor signals (vibration, temperature, pressure, flow rate).
- **Fully Containerized**:
  - Orchestrated via `docker-compose.yml` with 8 microservices, including automated model download.

---

## 🛠️ Technology Stack

| Layer | Technologies |
|---|---|
| **Frontend** | React 18, Vite, Lucide Icons, Microsoft SignalR Client, Modern Vanilla CSS |
| **Backend API** | .NET 9 ASP.NET Core Web API, SignalR Hubs, Background Hosted Services |
| **PDF Engine** | QuestPDF (SkiaSharp with Linux font rendering support) |
| **AI Inference** | Ollama Engine running `qwen2.5:1.5b` (Local CPU/GPU inference) |
| **Message Queue** | RabbitMQ 3 (AMQP 5672 + Management Dashboard 15672) |
| **Database** | TimescaleDB (PostgreSQL 16 + Timescale time-series hypertable extension) |
| **Database Explorer** | Adminer Web UI |
| **DevOps** | Docker, Docker Compose, Multi-stage Dockerfiles, Nginx Alpine |

---

## 🚀 Quick Start Guide

### Option A: One-Command Docker Setup (Recommended)

Run all 8 services in isolated containers with automatic model download and database seeding:

```bash
docker compose up --build
```

> **Note on First Run**: The `ollama-pull-model` container will automatically pull `qwen2.5:1.5b` (~980MB) into the shared Docker volume. Once the download finishes, the .NET backend and React frontend will start automatically.

To stop the containers:
```bash
docker compose down
```

---

### Option B: Local Development Setup

If you prefer developing locally with hot-reloading:

#### 1. Start Infrastructure Services (Database, RabbitMQ, Ollama)
```bash
docker compose up timescaledb seeder rabbitmq ollama -d
```

Pull the local AI model (if not already downloaded):
```bash
ollama pull qwen2.5:1.5b
```

#### 2. Run .NET 9 Backend
```bash
cd ".net backend/src/SensorReportBot.Api"
dotnet run
```
*API will start on `http://localhost:5000` (Swagger UI: `http://localhost:5000/swagger`).*

#### 3. Run React Vite Frontend
In a new terminal:
```bash
cd react-frontend
npm install
npm run dev
```
*Frontend will open on `http://localhost:5173`.*

---

## 🌐 Service Ports & Access URLs

| Service | URL / Port | Credentials / Details |
|---|---|---|
| **React Web App** | [http://localhost:5173](http://localhost:5173) | Main application UI & Chat |
| **.NET Backend API** | [http://localhost:5000](http://localhost:5000) | REST API & SignalR WebSockets endpoint |
| **RabbitMQ Management** | [http://localhost:15672](http://localhost:15672) | **User**: `guest` / **Pass**: `guest` |
| **Adminer (Database GUI)** | [http://localhost:8080](http://localhost:8080) | **System**: PostgreSQL<br>**Server**: `timescaledb` (or `localhost`)<br>**User**: `iam_user` / **Pass**: `iam_pass`<br>**DB**: `iam_db` |
| **Ollama Inference Engine** | [http://localhost:11434](http://localhost:11434) | Local LLM inference server |
| **TimescaleDB** | `localhost:5432` | PostgreSQL 16 time-series database |

---

## 💬 Conversational Chat Workflows

The AI assistant handles multi-turn report configuration in natural language:

### 1. Greetings & Catalog Inquiries
- User: `"hi"` or `"i want report"` or `"what machines are there?"`
- Bot: Returns the 5 monitored equipment options as interactive chips:
  - `Boiler Feed Pump Motor`
  - `Air Compressor Motor`
  - `Main Conveyor Drive Motor`
  - `Cooling Tower Fan Motor`
  - `Primary Crusher Motor`

### 2. Timeframe Flexibility
- Clicking a chip: Click `Air Compressor Motor` &rarr; prompts for timeframe with presets (`24h`, `5d`, `14d`, `30d`, `Custom Calendar Range`).
- Natural Language Durations: `"for last 3 days"`, `"past 2 weeks"`, `"yesterday"`, or `"last 24 hours"`.
- Concrete Calendar Dates: `"from 2026-09-07 to 2026-09-10"`.

### 3. One-Shot Requests
- User: `"Give me a full report for the boiler pump for the last 5 days"`
- Bot: Extracts both machine and timeframe simultaneously and presents the **Confirmation Card**:
  - `[Yes, Queue PDF Report]`
  - `[Change Options]`

### 4. Background Job Queueing & Direct Download
- Clicking **`Yes, Queue PDF Report`** places the task on the RabbitMQ queue (`pdf_report_jobs`).
- Real-time progress updates are shown in the chat and in the left sidebar queue manager.
- Once rendered, a **`Download PDF Report`** button appears right inside the chat bubble.

---

## 📂 Project Structure

```text
CFDH-AI/
├── .net backend/
│   ├── src/
│   │   ├── SensorReportBot.Api/             # Web API, Controllers, SignalR Hubs
│   │   ├── SensorReportBot.Application/     # CQRS, Step Pipelines, Workflows, DTOs
│   │   ├── SensorReportBot.Domain/          # Entities, Enums, Interfaces
│   │   └── SensorReportBot.Infrastructure/  # LLM Service, RabbitMQ, QuestPDF, Prompts
│   ├── Dockerfile                           # Multi-stage .NET 9 + QuestPDF Linux runtime
│   └── SensorReportBot.sln
├── react-frontend/
│   ├── src/
│   │   ├── App.jsx                          # Split-view UI (Sidebar, Telemetry, SignalR Chat)
│   │   ├── App.css                          # Dark-mode industrial glassmorphism design
│   │   └── main.jsx
│   ├── nginx.conf                           # Production Nginx reverse proxy configuration
│   └── Dockerfile                           # Multi-stage Node 20 + Nginx Alpine build
├── iam-seed/
│   ├── init.sql                             # Database schema & initial tables
│   └── Dockerfile                           # Auto-seeder container
├── docker-compose.yml                       # Full 8-service container orchestration
└── README.md                                # Project documentation
```

---

## 📄 License
This project is licensed under the MIT License.
