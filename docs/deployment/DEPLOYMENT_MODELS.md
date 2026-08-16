# Deployment Models Guide: Choosing the Right Path for Your Role

This guide helps you choose the right deployment and development path based on your role, technical background, and use case.

## Quick Decision Tree

```
┌─────────────────────────────────┐
│  What is your primary goal?     │
└────────────┬────────────────────┘
             │
    ┌────────┴────────┬──────────────┬──────────────┐
    │                 │              │              │
    ▼                 ▼              ▼              ▼
"I want to       "I'm contributing "I run on      "I need to
run this on      or architecting   Linux, want   understand
my home server"  new features"     production"   the system"
    │                 │              │              │
    │                 │              │              │
    ▼                 ▼              ▼              ▼
Compose-First   Aspire Dev      Compose-Only    Aspire +
(This guide)    (See #262.2)    (See #262.3)    Docs
                                                (See #262.4)
```

## Persona 1: The Self-Hoster (Compute Owner)

**Profile:**
- Non-technical or systems-level technical
- Owns an on-prem Linux server (NAS, repurposed laptop, Raspberry Pi)
- Wants a private, self-contained knowledge system
- No interest in code or architecture
- Expects: "Install and forget" experience

**Deployment Path: Docker Compose**

### Prerequisites

```
✓ Docker and Docker Compose installed (v2.x+)
✓ 8GB RAM minimum, 50GB+ storage
✓ Basic shell/terminal comfort (minimal)
✓ A `.env` file template (provided)
✓ 15 minutes of one-time setup
```

### Setup Steps

```bash
# 1. Clone or download the repository
git clone https://github.com/matthewcorven/streaming-digest.git
cd streaming-digest

# 2. Copy environment template
cp .env.example .env

# 3. Edit .env with your choices (documented in-file)
# - PostgreSQL admin password
# - OpenAI API key (if using cloud embedding/LLM)
# - Ingestion schedule (default: 6 AM daily)
# - Ollama model choices (default: bge-m3 for embeddings)
nano .env  # or your preferred editor

# 4. Start the stack
docker compose up -d

# 5. Wait for startup (PostgreSQL + Ollama model download takes 2-5 min)
docker compose logs -f streaming-digest-api

# 6. Access the UI
# Open browser to: http://your-server-ip:5000
# (Or via Tailscale if on restricted network)
```

### What Happens Next

✓ PostgreSQL starts first (no external dependencies)
✓ Ollama starts, pulls configured embedding model (~2-10 min depending on model)
✓ API starts, waits for PostgreSQL + Ollama ready
✓ Worker starts, waits for API + PostgreSQL ready
✓ Whisper starts (audio-to-text)
✓ First ingestion runs at scheduled time (6 AM by default)
✓ Logs appear in terminal/docker compose logs

### Key Configuration

| Setting | Default | Meaning |
|---------|---------|---------|
| `DB_PASSWORD` | (required) | PostgreSQL admin password. Change it! |
| `INGESTION_TIME` | 6 | Hour of day (0-23) to run automatic ingestion (server local time) |
| `OLLAMA_MODEL_EMBEDDING` | `bge-m3` | Embedding model. Other options: `nomic-embed-text`, `all-minilm` |
| `OLLAMA_MODEL_LLM` | `mistral` | LLM for chat/analysis. Other options: `neural-chat`, `orca-mini` |
| `ASPIRE_ALLOW_UNSECURED_TRANSPORT` | `true` | Allow HTTP (dev only). Set to `false` in production. |

### Common Tasks

**Check status:**
```bash
docker compose ps
# Shows which containers are Running/Exited/Restarting
```

**View logs:**
```bash
docker compose logs -f streaming-digest-api
# Shows live logs from API container

docker compose logs streaming-digest-worker | tail -50
# Shows recent worker logs
```

**Stop the stack:**
```bash
docker compose down
# Stops all containers (data persisted in volumes)
```

**Restart a service:**
```bash
docker compose restart streaming-digest-api
# Restarts API container (preserves data)
```

**Access Grafana dashboards (if enabled):**
```
http://localhost:3000
# Default credentials: admin/admin (change immediately in production)
```

**Backup your data:**
```bash
docker compose exec streaming-digest-postgres pg_dump -U postgres streaming_digest > backup.sql
# Exports all data to backup.sql
```

### Troubleshooting

**"Port 5000 already in use"**
- Change port in docker-compose.yaml: `ports: ["5001:5000"]`
- Or: `docker ps` to find what's using port 5000, then stop it

**"PostgreSQL fails to start"**
- Check volume permissions: `docker compose down -v` (⚠️ deletes data)
- Then: `docker compose up -d` (fresh start)

**"Ollama download takes too long"**
- Check network: `docker compose logs streaming-digest-ollama`
- Default model (~7GB) takes 15-30 min over 10 Mbps connection
- Smaller model: Set `OLLAMA_MODEL_EMBEDDING=all-minilm` (~40MB, faster)

**"Ingestion never starts"**
- Check Hangfire dashboard: http://localhost:5000/admin/jobs
- Verify ingestion time matches your server timezone
- Worker logs: `docker compose logs streaming-digest-worker | tail -100`

**"Search returns no results or errors"**
- API logs: `docker compose logs streaming-digest-api | grep -i search`
- Ensure Ollama is running: `docker compose ps streaming-digest-ollama`
- Restart Ollama: `docker compose restart streaming-digest-ollama`

### Performance Tuning

**For slow searches:**
- Reduce LIMIT in PostgreSQL queries (docs/operations/PERFORMANCE_BASELINE.md)
- Ensure Ollama model is pinned to CPU vs GPU (check docker-compose.yaml)

**For slow ingestion:**
- Increase `WORKER_CONCURRENCY` in `.env` (default: 1)
- Monitor PostgreSQL with: `docker compose exec streaming-digest-postgres psql -U postgres -d streaming_digest -c "SELECT * FROM pg_stat_statements;"`

**For memory pressure:**
- Check container resource limits: `docker stats`
- Reduce Ollama memory: Set `OLLAMA_NUM_GPU=-1` to force CPU (slower but uses less RAM)

---

## Persona 2: The Contributor (Platform Developer)

**Profile:**
- Software developer or platform engineer
- Contributes code, investigates bugs, designs features
- Needs live dashboards, fast feedback loops, inspection tools
- Comfortable with .NET, C#, Docker, and Git
- Expects: Full visibility into system internals

**Deployment Path: Aspire AppHost**

### Prerequisites

```
✓ .NET 10 SDK installed
✓ Docker Desktop (or Docker Engine on Linux)
✓ Git
✓ VS Code or Visual Studio
✓ 15-30 minutes initial setup
```

### Setup Steps

```bash
# 1. Clone the repository
git clone https://github.com/matthewcorven/streaming-digest.git
cd streaming-digest

# 2. Restore NuGet packages
dotnet restore

# 3. Start Aspire AppHost
dotnet run --project src/StreamingDigest.AppHost

# 4. Aspire Dashboard opens automatically
# Or navigate to: http://localhost:18888

# 5. Dashboard shows:
#   - All services (API, Worker, PostgreSQL, Ollama, Whisper)
#   - Live logs streamed from each container
#   - Resource state (Running/Degraded/Error)
#   - Trace visualization (Tempo)
#   - Metrics (Prometheus)
```

### Aspire Dashboard Features

| Feature | Benefit | How to Use |
|---------|---------|-----------|
| **Resource Graph** | See service dependencies & health at a glance | Main "Resources" tab |
| **Live Logs** | Follow execution in real-time, filter by service | Click service → "Logs" |
| **Traces** | Distributed tracing across API → Worker → DB → Ollama | Click "Traces" tab |
| **Metrics** | CPU, memory, network per container | "Metrics" tab (if Prometheus enabled) |
| **Environment Variables** | Inspect injected config without SSH/exec | Click service → "Environment" |
| **Structured Logs** | JSON-formatted logs with context (trace ID, severity) | Logs tab with filters |

### Key Development Workflows

**Investigating a bug:**
```
1. Start Aspire: dotnet run --project src/StreamingDigest.AppHost
2. Reproduce the bug in the UI
3. Check worker logs in Aspire: Look for ERROR or WARN entries
4. Check traces: Aspire → Traces tab → Find the request
5. View database query: Expand trace spans for SQL execution time
6. Edit code to fix bug
7. dotnet watch will auto-restart the affected service
```

**Adding a feature:**
```
1. Make code changes (e.g., new API endpoint)
2. dotnet watch detects changes → rebuilds
3. Aspire auto-restarts affected service (API)
4. Test in UI immediately
5. Check logs in Aspire for any errors
6. Commit and push
```

**Testing integration with Ollama:**
```
1. Aspire started with Ollama running
2. Call any endpoint that uses embeddings (e.g., search)
3. Aspire → Traces tab → Find the request
4. Expand trace span for "OllamaSharp.OllamaApiClient.GenerateEmbeddingAsync"
5. See embedding latency, model used, dimensions returned
6. Modify config in AppHost.cs if needed (model choice, etc.)
```

**Performance profiling:**
```
1. Aspire Dashboard → Metrics tab
2. Watch CPU/memory as you run ingestion
3. Check PostgreSQL query performance:
   docker compose exec streaming-digest-postgres psql -U postgres
   SELECT query, calls, mean_time FROM pg_stat_statements ORDER BY mean_time DESC;
4. Optimize hot queries
```

### Configuration for Development

AppHost automatically reads configuration from:

1. **User Secrets** (highest priority, local machine only)
   ```bash
   dotnet user-secrets set "OpenAI:ApiKey" "sk-..."
   dotnet user-secrets set "Ingestion:ScheduleTime" "6"
   ```

2. **Environment Variables**
   ```bash
   export ASPIRE_ALLOW_UNSECURED_TRANSPORT=true
   dotnet run --project src/StreamingDigest.AppHost
   ```

3. **AppHost.cs defaults** (lowest priority)
   ```csharp
   var db = builder
       .AddPostgres("postgres")
       .AddDatabase("streaming-digest", dbName: "streaming_digest");
   ```

### Hot Reload Capability

Enable fast iteration:

```bash
# Start with watch mode (auto-rebuild on file changes)
dotnet watch --project src/StreamingDigest.AppHost run

# Or just: dotnet run --project src/StreamingDigest.AppHost
# (If watch is configured in .vscode/launch.json)
```

**How it works:**
- File change detected (C# code, .cshtml, CSS)
- Project rebuilds
- Service restarts (keeping data in volumes)
- Aspire Dashboard updates status
- Next request uses new code

**What persists:**
- PostgreSQL data (volume mount)
- Ollama model cache (volume mount)
- Session state (in-memory, resets on restart)

### Troubleshooting Aspire

**"Ollama takes forever to start"**
- Check `docker images | grep ollama`
- If not present, Aspire will pull it (5-10 GB download)
- First startup always slow
- Subsequent runs use cached image

**"Port 18888 (Dashboard) already in use"**
- Change in AppHost.cs: `.WithHttpEndpoint(port: 18889)`
- Or: Kill process using port 18888

**"Service won't start (Degraded)"**
- Aspire → Click service → Check logs
- Common: PostgreSQL password wrong, Ollama out of disk
- Fix issue, then: Restart service via dashboard or `docker compose restart`

**"Hot reload not triggering"**
- Verify dotnet watch is running: Check terminal for "Watching for file changes"
- Edit a .cs file and save → Should see "Restarting" message
- If not: Kill dotnet process, restart with `dotnet watch`

---

## Persona 3: The Production Admin (Operations)

**Profile:**
- Manages Streaming Digest on Linux server (in production)
- Not a developer, but comfortable with shell commands
- Needs to update deployment, check health, handle failures
- Expects: Simple, documented procedures

**Deployment Path: Docker Compose (Production-Hardened)**

### Initial Deployment

```bash
# 1. Get compose.yaml from latest release
wget https://github.com/matthewcorven/streaming-digest/releases/download/v1.0.0/docker-compose.yaml

# 2. Get environment template
wget https://github.com/matthewcorven/streaming-digest/releases/download/v1.0.0/.env.example
cp .env.example .env

# 3. Configure (see Persona 1 for details)
nano .env

# 4. Start
docker compose up -d

# 5. Verify
docker compose ps
docker compose logs streaming-digest-api | tail -20
```

### Routine Maintenance

**Daily:**
- Check that ingestion completed: `docker compose logs streaming-digest-worker | grep "ingestion completed"`
- Monitor disk usage: `docker volume ls` and `df -h`

**Weekly:**
- Review error logs: `docker compose logs --since 7d | grep -i error`
- Check PostgreSQL size: `docker compose exec streaming-digest-postgres psql -U postgres -c "SELECT pg_database.datname, pg_size_pretty(pg_database_size(pg_database.datname)) AS size FROM pg_database;"`

**Monthly:**
- Backup database: See backup steps in Persona 1
- Update container images: `docker compose pull && docker compose up -d`
- Review observability data (Grafana)

### Upgrade Procedures

**Before upgrading:**
1. Backup database (see Persona 1)
2. Note current version: `docker compose images`

**Upgrade process:**
```bash
# 1. Get new docker-compose.yaml
wget -O compose.yaml.new https://github.com/matthewcorven/streaming-digest/releases/download/v1.1.0/docker-compose.yaml

# 2. Compare with current
diff compose.yaml compose.yaml.new

# 3. Backup current
cp compose.yaml compose.yaml.backup

# 4. Update
mv compose.yaml.new compose.yaml

# 5. Pull new images
docker compose pull

# 6. Start upgraded stack (with minimal downtime)
docker compose down
docker compose up -d

# 7. Verify
docker compose ps
docker compose logs streaming-digest-api | tail -30
```

### Health Checks & Alerts

**Manual health check:**
```bash
# API is responsive
curl -s http://localhost:5000/health | jq .

# All services running
docker compose ps | grep -c "Up" | xargs -I {} bash -c 'test {} -ge 5 && echo "OK" || echo "DOWN"'

# PostgreSQL is responsive
docker compose exec streaming-digest-postgres psql -U postgres -c "SELECT 1;" > /dev/null && echo "DB OK" || echo "DB DOWN"

# Ollama is ready
curl -s http://localhost:11434/api/tags | jq '.models | length' 
```

**Setting up alerts (example with cron):**
```bash
# Create script: /opt/check-health.sh
#!/bin/bash
curl -s http://localhost:5000/health | jq -e '.status == "running"' || \
  mail -s "ALERT: Streaming Digest health check failed" admin@example.com

# Add to crontab
0 * * * * /opt/check-health.sh  # Check every hour
```

### Disaster Recovery

**Service down: API**
```bash
docker compose restart streaming-digest-api
# Wait 30 seconds for startup
curl http://localhost:5000/health
```

**Service down: Worker (ingestion stuck)**
```bash
docker compose restart streaming-digest-worker
# Ingestion jobs will resume from queue
```

**Service down: PostgreSQL (data loss risk!)**
```bash
# STOP: Don't restart blindly
# 1. Check logs first
docker compose logs streaming-digest-postgres | tail -50

# 2. Restore from backup if corrupted
docker compose down
docker volume rm streaming-digest_postgres-data  # ⚠️ Destructive
docker compose up -d
docker compose exec streaming-digest-postgres psql -U postgres streaming_digest < backup.sql

# 3. Or: Extend volume if disk full
# (More complex, see docs/operations/UPGRADE_PATHS.md)
```

---

## Persona 4: The Architect (System Designer)

**Profile:**
- Designing the system, reviewing architecture, planning changes
- Not actively running code, but need to understand all paths
- Interested in trade-offs, scalability, operational guarantees
- Expects: Comprehensive mental model

**Recommended Reading Path:**

1. **Start here:** `docs/architecture/ARCHITECTURE.md` (§1-5)
   - High-level goals, services, logical design

2. **Then:** `docs/architecture/DEPLOYMENT_ARCHITECTURE.md`
   - Service interactions, topologies, failure scenarios

3. **Deep-dive:** `docs/operations/UPGRADE_PATHS.md`
   - Configuration, secrets, schema migration

4. **Aspire wiring:** `src/StreamingDigest.AppHost/Program.cs`
   - How services are actually composed

5. **ADRs:** `docs/adr/` directory
   - Architectural decisions and trade-offs

### Key Design Questions

**Q: Can Streaming Digest scale horizontally?**
- A: API is stateless (can run multiple replicas)
- A: Worker uses Hangfire (distributed job queue, supports multiple workers)
- A: PostgreSQL bottleneck (single DB, would need multi-node solution)
- **Recommendation:** Multi-replica API + multi-worker is straightforward; DB scaling is future work

**Q: What happens if Ollama is unavailable?**
- A: Ingestion pauses at embedding stage (no fallback)
- A: Search without vector falls back to text-only
- **Recommendation:** Monitor Ollama health; use model readiness guards (ADR-0004)

**Q: How do we ensure data consistency?**
- A: PostgreSQL ACID guarantees within single-machine deployment
- A: Outbox pattern for event publishing (Hangfire + DB transaction)
- A: No distributed transactions across services
- **Recommendation:** Works for single-host; multi-region would need CRDT or event sourcing

**Q: Configuration & secrets strategy?**
- A: Environment variables (Docker Compose) or user-secrets (Aspire dev)
- A: PostgreSQL for operational/durable config
- A: Schema migrations via migration tool (see UPGRADE_PATHS.md)
- **Recommendation:** Good for small-to-medium deployments; large orgs would use vault/secret manager

See `docs/adr/` for deeper architectural decisions (embedding model choice, vector indexing, etc.).

---

## Summary: Choosing Your Path

| Aspect | Self-Hoster | Contributor | Production Admin | Architect |
|--------|-------------|-------------|------------------|-----------|
| **Primary Tool** | Docker Compose | Aspire Dashboard | Shell + Docker | Docs + Code |
| **Time to First Run** | 15 min | 30 min | 20 min | N/A |
| **Configuration** | `.env` file | User-secrets + AppHost | `.env` + docker-compose.yaml | Source code |
| **Debugging** | `docker logs` | Aspire Dashboard | `docker logs` + Grafana | Architecture docs |
| **Upgrade Path** | `docker compose pull` | `git pull` | `docker compose pull` | Review ADRs |
| **Learning Curve** | Low | Medium | Low-Medium | High |

**Next Steps:**
- Self-Hoster → Follow setup in #262.1 (Docker Compose Quickstart)
- Contributor → Follow setup in #262.2 (Aspire Development)
- Production Admin → Follow setup in #262.3 (Production Checklist)
- Architect → Dive into #262.4 (Architecture Review & Design)

---

## Cross-Reference

- **#261:** Deployment options epic
- **#262:** This guide (deployment models for personas)
- **#263:** Compose regeneration & Aspire publish workflow
- **#264:** Advanced scenarios (Kubernetes, multi-machine, GPU acceleration)
- **#265:** Configuration continuity & migration procedures
