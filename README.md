# AudioYotoShelf

Bridge your [Audiobookshelf](https://www.audiobookshelf.org/) library to [Yoto](https://www.yotoplay.com/) Make Your Own (MYO) cards.

Transfer audiobooks from your self-hosted Audiobookshelf server to Yoto MYO cards with auto-generated pixel art chapter icons, series-to-playlist mapping, and age range suggestions.

## Features

- **Library browsing** — Browse your ABS library with book/series views
- **One-click transfers** — Download from ABS, upload to Yoto, create card automatically
- **Chapter icon generation** — AI-generated 16×16 pixel art icons via Gemini, or use Yoto's public icon library
- **Age range suggestions** — Automatic age range inference from metadata with user override
- **Series support** — Transfer entire series as individual cards
- **Real-time progress** — SignalR-powered live transfer status updates
- **Background processing** — Hangfire job queue with dashboard
- **Per-user permissions** — Respects ABS library access controls

## Architecture

```
┌───────────────┐    ┌──────────────────────┐    ┌───────────────┐
│ Audiobookshelf│◄───│   AudioYotoShelf     │───►│  Yoto MYO API │
│   Server      │    │                      │    │               │
└───────────────┘    │  .NET 10 API         │    └───────────────┘
                     │  Vue 3 SPA           │
                     │  PostgreSQL 17       │    ┌───────────────┐
                     │  Redis 7             │───►│  Gemini 2.5   │
                     │  Hangfire            │    │  (Icons)      │
                     └──────────────────────┘    └───────────────┘
```

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Backend | .NET 10, C# 14, EF Core 10 |
| Frontend | Vue 3.5, TypeScript, Vite 7, Pinia 3, Tailwind CSS |
| Database | PostgreSQL 17 |
| Cache | Redis 7 |
| Jobs | Hangfire |
| Real-time | SignalR |
| Icons | Gemini 2.5 Flash + SixLabors.ImageSharp |
| Audio | FFmpeg (chapter extraction) |

## Quick Start

### Prerequisites

- Docker and Docker Compose
- [Yoto Developer API credentials](https://developers.yotoplay.com)
- (Optional) [Gemini API key](https://aistudio.google.com/apikey) for icon generation

### Setup

```bash
# Clone the repo
git clone https://github.com/youruser/AudioYotoShelf.git
cd AudioYotoShelf

# Create environment file
cp .env.example .env
# Edit .env with your credentials

# Build and run
docker compose up -d

# Open in browser
open http://localhost:8080
```

### First Run

1. Open `http://localhost:8080` and enter your Audiobookshelf server URL, username, and password
2. Authorize with Yoto via the OAuth device flow
3. Browse your library and transfer books to MYO cards

## Development

### Prerequisites

- .NET 10 SDK
- Node.js 22 LTS
- PostgreSQL 17 (or use Docker)
- Redis 7 (or use Docker)

### Backend

```bash
cd src/AudioYotoShelf.Api
dotnet run
```

### Frontend

```bash
cd src/AudioYotoShelf.ClientApp
npm install
npm run dev
```

The Vite dev server runs on port 5173 and proxies `/api` and `/hubs` to the .NET backend on port 5000.

### Tests

```bash
dotnet test
```

## Proxmox LXC Deployment

For Proxmox users running Docker inside an LXC container:

```bash
# On Proxmox host: enable nesting and keyctl
pct set <CTID> -features keyctl=1,nesting=1

# Inside LXC: install Docker
curl -fsSL https://get.docker.com | sh

# Deploy
cd /opt/audioyotoshelf
docker compose up -d
```

Recommended LXC resources: 4 cores, 4GB RAM, 60GB disk.

## Project Structure

```
AudioYotoShelf/
├── src/
│   ├── AudioYotoShelf.Api/          # ASP.NET Core API + SignalR hub
│   ├── AudioYotoShelf.Core/         # Domain entities, interfaces, DTOs
│   ├── AudioYotoShelf.Infrastructure/ # EF Core, API clients, services
│   └── AudioYotoShelf.ClientApp/    # Vue 3 SPA
├── tests/
├── Dockerfile
├── docker-compose.yml
└── AudioYotoShelf.sln
```

## License

MIT
