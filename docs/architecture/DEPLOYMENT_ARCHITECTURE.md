# Deployment Architecture: Service Interactions and Topologies

This guide documents service interactions, deployment topologies, and component relationships that underpin the self-hosting deployment paths.

## Overview

Streaming Digest separates concerns into discrete services that communicate over HTTP and share persistent data through PostgreSQL. The architecture supports two primary deployment paths:

1. **Local Development** — Aspire AppHost orchestration with live dashboards
2. **Production Self-Hosting** — Docker Compose deployment with static configuration

## Service Topology

### Core Services

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│  ┌──────────────────┐         ┌──────────────────┐             │
│  │  Blazor UI + API │◄────────┤   PostgreSQL     │             │
│  │ streaming-digest-│         │ streaming-digest-│             │
│  │      api         │         │     postgres     │             │
│  └────────┬─────────┘         └────────▲─────────┘             │
│           │                           │                        │
│           │ (REST API calls)          │ (SQL queries)         │
│           │                           │                        │
│  ┌────────▼────────────────────────────┴─────────┐             │
│  │                                               │             │
│  │       Background Job Queue (Hangfire)        │             │
│  │                                               │             │
│  └────────▲────────────────────────────┬────────┘             │
│           │                           │                        │
│  ┌────────┴──────────┐         ┌──────▼──────────┐             │
│  │   Worker Service  │         │  Scheduled Jobs │             │
│  │ streaming-digest- │         │  (Ingestion)    │             │
│  │     worker        │         │                │             │
│  └────────┬──────────┘         └──────────────────┘             │
│           │                                                    │
│           │ (HTTP requests for models & inference)            │
│           │                                                    │
│  ┌────────▼──────────┐    ┌──────────────┐                    │
│  │  Ollama Service   │    │   Whisper    │                    │
│  │ streaming-digest- │    │  streaming-  │                    │
│  │    ollama         │    │   digest-    │                    │
│  │                  │    │  whisper     │                    │
│  │ (Embeddings,     │    │              │                    │
│  │  LLM inference)  │    │ (Audio→Text) │                    │
│  └───────────────────┘    └──────────────┘                    │
│                                                                 │
│  Observability Stack (Prometheus, Loki, Tempo, OpenTelemetry)  │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### Data Flow Patterns

#### Ingestion Flow (User initiates via UI)

```
1. User clicks "Add Channel" or "Sync Now" in Blazor UI
   │
   ├─► API validates input & persists channel/video metadata to PostgreSQL
   │
   ├─► API queues Hangfire job (via outbox pattern)
   │
   └─► API returns 202 (Accepted) to UI
       │
       └─► UI polls /api/ingestion/status for job progress
           │
           ├─► Worker picks up job from Hangfire queue
           │
           ├─► Worker fetches video metadata (yt-dlp, transcript providers)
           │
           ├─► Worker calls Whisper service for audio-to-text (if needed)
           │
           ├─► Worker calls Ollama for embeddings & classification
           │
           ├─► Worker writes results + Domain Events to PostgreSQL
           │
           ├─► Worker updates ingestion status (completed/failed/paused)
           │
           └─► UI reflects status change (next polling cycle)
```

#### Search Flow (User queries vector/full-text)

```
1. User enters search query in Blazor UI
   │
   ├─► UI sends query to /api/search/hybrid
   │
   ├─► API receives hybrid search request (text + optional vector)
   │
   ├─► If vector search:
   │   └─► API calls Ollama /api/embeddings to generate query embedding
   │
   ├─► API executes PostgreSQL hybrid query:
   │   ├─► Full-text search (tsvector index on PostgreSQL)
   │   ├─► Vector similarity search (pgvector similarity over embeddings)
   │   └─► Merges results with relevance scoring
   │
   ├─► API returns ranked results to UI
   │
   └─► UI renders search results (title, description, snippet, relevance score)
```

#### Health Monitoring Flow

```
1. Aspire Dashboard or Docker Compose health probes (every 30s)
   │
   ├─► API health probe: GET /health
   │   └─► Checks PostgreSQL connectivity, Ollama availability, Whisper readiness
   │
   ├─► Worker health: Hangfire recurring job execution timestamps
   │
   ├─► PostgreSQL: Standard Docker HEALTHCHECK or dedicated probe container
   │
   ├─► Ollama: GET /api/tags (list available models)
   │
   └─► Whisper: GET /health or container-level healthcheck
       │
       └─► Aspire/Compose updates resource state (Running/Degraded/Error)
```

## Service Communication Matrix

| From | To | Protocol | Purpose | Auth |
|------|----|---------|---------|----|
| Blazor UI | API | HTTP/REST | Commands, queries, uploads | Session cookie |
| API | PostgreSQL | TCP (port 5432) | Data read/write | Environment password |
| Worker | PostgreSQL | TCP (port 5432) | Data read/write, Hangfire | Environment password |
| Worker | Ollama | HTTP/REST | Embeddings, LLM inference | None (internal) |
| Worker | Whisper | HTTP/REST | Audio transcription | None (internal) |
| API | Ollama | HTTP/REST | Query embedding (search) | None (internal) |
| Aspire Dashboard | API | HTTP/REST | Health, observability | None (local dev) |
| OpenTelemetry Collector | Services | gRPC/HTTP | Traces, metrics, logs | None (internal) |

## Deployment Topology Variants

### Topology 1: Single-Host Docker Compose (Standard Self-Hosting)

**Use case:** Novice users, single on-prem Linux server, private/restricted network

```
┌─────────────────────────────────────────┐
│   Linux Docker Host (on-prem)           │
│   • Single machine                       │
│   • 8GB+ RAM, 50GB+ storage              │
│   • Behind Tailscale or firewall         │
│                                         │
│   ┌─────────────────────────────────┐   │
│   │ Docker Compose Stack             │   │
│   │                                 │   │
│   │  [API]  [Worker]  [Postgres]   │   │
│   │  [Ollama]  [Whisper]            │   │
│   │  [Prometheus/Loki/Tempo]        │   │
│   │                                 │   │
│   └─────────────────────────────────┘   │
│                                         │
│   Volumes:                               │
│   • postgres-data (persists db)          │
│   • ollama-models (model cache)          │
│   • transcripts-cache                    │
│   • screenshots-cache                    │
│                                         │
│   Networking: bridge (internal-only)     │
│   Ports: 80/443 (Tailscale TUN)         │
└─────────────────────────────────────────┘
```

**Deployment command:**
```bash
./scripts/publish_compose.sh  # Regenerate from AppHost
docker compose up -d
docker compose logs -f streaming-digest-api
```

**Configuration:**
- `.env` file for secrets, API keys, ingestion schedule
- `compose.yaml` auto-generated from AppHost (git-tracked)
- Persistence via Docker named volumes

### Topology 2: Local Aspire Development

**Use case:** Contributors, developers, architecture review, local testing

```
┌─────────────────────────────────────────┐
│   Developer Workstation                 │
│   • macOS/Windows/Linux                 │
│   • .NET 10 SDK                         │
│   • Docker Desktop                      │
│                                         │
│   ┌─────────────────────────────────┐   │
│   │ Aspire AppHost (dotnet run)      │   │
│   │                                 │   │
│   │ • Manages service lifecycle     │   │
│   │ • Injects config & secrets      │   │
│   │ • Exposes Aspire Dashboard      │   │
│   │   (localhost:18888)             │   │
│   │ • Logs aggregated to console    │   │
│   │                                 │   │
│   │ Managed Services:               │   │
│   │  [API]  [Worker]  [Postgres]   │   │
│   │  [Ollama]  [Whisper]            │   │
│   │                                 │   │
│   └─────────────────────────────────┘   │
│                                         │
│   Aspire Dashboard Features:             │
│   • Resource health & resource graphs   │
│   • Live logs streamed to console       │
│   • Trace visualization (Tempo)         │
│   • Metrics (Prometheus)                │
│   • Environment variable inspection     │
│                                         │
│   Hot Reload:                           │
│   • dotnet watch detects code changes   │
│   • Services rebuild & restart          │
│   • Session state preserved via volume  │
└─────────────────────────────────────────┘
```

**Development command:**
```bash
dotnet run --project src/StreamingDigest.AppHost
# Opens Aspire Dashboard at http://localhost:18888
```

**Key differences from Compose:**
- Aspire manages container lifecycle (no manual `docker compose up`)
- Tighter dev feedback loop (live logs, metrics, traces in one dashboard)
- Hot reload capability for code iteration
- Config injected at runtime (no `.env` file needed during dev)

### Topology 3: Kubernetes Deployment (Future)

**Use case:** Enterprise self-hosting, high availability, multi-node orchestration

**Not currently supported**, but architecture supports it via:
- Stateless API service (horizontally scalable)
- Separate PostgreSQL deployment (operator-managed)
- Separate Ollama deployment (can use GPU nodes)
- ConfigMaps for config, Secrets for sensitive data

## Configuration Continuity: Dev → Production

### Configuration Sources (Priority Order)

1. **Aspire (dev) / Docker Compose (prod)** — Infrastructure wiring, port mappings, volume mounts
2. **.env file (prod only)** — Secrets, API keys, service endpoints, bootstrap parameters
3. **PostgreSQL app_settings** — Durable runtime configuration, user-facing settings, operational state

### Configuration Flow Diagram

```
┌─────────────────────┐
│ Aspire AppHost      │ (Development)
│ C# service defs     │ • Defines services, dependencies
│ WithEnvironment()   │ • Wires secrets from user-secrets
│ WithVolume()        │ • Sets port bindings & volumes
└────────────┬────────┘
             │
             │ aspire publish docker-compose
             ▼
┌─────────────────────┐
│ docker-compose.yaml │ (Generated)
│ services:           │ • Declarative service definitions
│   api:              │ • Port mappings, volume refs
│     image:          │ • Environment variable templates
│     environment:    │ • Depends-on relationships
│     volumes:        │
│     depends_on:     │
└────────────┬────────┘
             │
             │ (+ manual edits not recommended)
             ▼
┌─────────────────────┐
│ .env file (prod)    │ (Secrets & Deploy Config)
│ DB_PASSWORD=xxx     │ • Sensitive data (never git)
│ API_KEY=yyy         │ • Service-specific API keys
│ INGESTION_TIME=6am  │ • Deployment-specific settings
│ OLLAMA_ENDPOINT=... │
└────────────┬────────┘
             │
             │ docker compose up -d
             ▼
┌──────────────────────────────┐
│ Running Containers           │
│ Environment resolved:        │
│ • Compose env vars injected  │
│ • .env vars merged into proc │
│ • Config mapped to AppSettings
└──────────────────────────────┘
             │
             ▼
┌──────────────────────────────┐
│ PostgreSQL app_settings      │ (Persistent Config)
│ (First-run writes)           │
│ • Ingestion schedule         │
│ • Embedding model choice     │
│ • User settings              │
│ • Operational state          │
└──────────────────────────────┘
```

## Service Lifecycle & Startup Order

### Startup Dependencies (from compose.yaml)

```yaml
services:
  api:
    depends_on:
      - postgres  # Wait for PostgreSQL
      - ollama    # Wait for Ollama (health check)
  
  worker:
    depends_on:
      - postgres  # Wait for PostgreSQL
      - ollama    # Wait for Ollama (health check)
  
  postgres:
    # No dependencies
    # First to start
  
  ollama:
    # No dependencies (except implicit: needs host GPU/CPU)
    # Starts early, may take time for model warm-up
  
  whisper:
    # No dependencies
```

### Readiness Checks

Each service implements health checks to signal readiness:

```
PostgreSQL
  └─► SELECT 1 (TCP 5432)

Ollama
  └─► GET /api/tags (HTTP 11434)
      • Returns available models
      • Ensures service is responsive

API
  └─► GET /health (HTTP 5000)
      ├─► Checks PostgreSQL connectivity
      ├─► Checks Ollama availability
      └─► Returns 200 OK when ready

Worker
  └─► Implicit via Hangfire heartbeat
      • First job execution = readiness signal
```

Aspire/Compose waits for `depends_on` services' health checks before proceeding to dependent services.

## Cross-Service Failure Scenarios

### Scenario 1: Ollama Becomes Unavailable

**User Experience:**
- Search queries fail (vector embedding unavailable)
- Ingestion pauses at embedding stage
- API returns 503 Service Unavailable for embedding routes
- Worker retries on schedule (configurable backoff)

**Recovery:**
- Admin restarts Ollama container: `docker compose restart streaming-digest-ollama`
- Worker resumes ingestion after next retry interval
- Search resumes once Ollama is responsive

**Mitigation:**
- Model readiness guard pre-checks availability before dispatch (ADR-0004)
- Fallback to text-only ingestion if configured
- Alert on Ollama health degradation

### Scenario 2: PostgreSQL Disk Full

**User Experience:**
- All writes fail (ingestion, search index updates)
- API returns 500 Internal Server Error
- UI shows connection errors

**Recovery:**
- Admin cleans up old runs/intermediate data
- Extends volume size (docker volume inspect, mount host partition)
- Restarts API & Worker services

### Scenario 3: Worker Service Crashes

**User Experience:**
- Scheduled ingestion doesn't run
- Manual job submissions don't start
- User sees "Job Queued" but never transitions to "Running"

**Recovery:**
- Admin checks logs: `docker compose logs streaming-digest-worker`
- Restarts worker: `docker compose restart streaming-digest-worker`
- Unprocessed jobs re-pick up from Hangfire queue

## Networking & Security

### Internal Communication (Docker Network)

- All services talk over internal bridge network (not exposed to host)
- Service-to-service discovery via container names (e.g., `postgres:5432`)
- No authentication between services (network isolation is primary control)

### External Access

- API exposed on host port (80/443 via reverse proxy or Tailscale)
- Blazor UI accessed via authenticated session cookie
- Admin endpoints (Hangfire dashboard) require session auth

### Secrets Management

- PostgreSQL passwords: Environment variables (`.env` file)
- API keys: Environment variables (`.env` file)
- Session keys: Auto-generated on first startup, stored in database
- Ollama API: No auth needed (internal network, not exposed)

See `docs/operations/UPGRADE_PATHS.md` for secret rotation and management procedures.

## Resource Sizing & Planning

### Minimum Hardware Requirements

- **CPU:** 2 cores (4 cores recommended for parallel ingestion)
- **Memory:** 8GB (16GB if running large embedding models locally)
- **Storage:** 50GB+ (depends on video library size and model cache)
- **Network:** 10 Mbps stable connection (YouTube video scraping)

### Container Resource Limits (recommended)

| Service | CPU | Memory | Rationale |
|---------|-----|--------|-----------|
| API | 1000m | 2GB | ASP.NET Core + Blazor |
| Worker | 2000m | 4GB | Parallel job processing |
| PostgreSQL | 500m | 2GB | Connection pooling, query execution |
| Ollama | 2000m+ | 4GB+ | Model inference (GPU if available) |
| Whisper | 2000m | 2GB | Audio processing (CPU-intensive) |
| **Total** | **7500m+** | **14GB+** | Single-host deployment |

### Storage Breakdown

- **PostgreSQL data:** 10-50GB (depends on run history, embeddings)
- **Ollama models:** 5-10GB (typical embedding model + LLM)
- **Whisper model:** 3-5GB (large multi-language model)
- **Screenshots cache:** 10-50GB (depends on ingestion scope)
- **Transcript cache:** 1-5GB (text storage)
- **Observability data:** 5-10GB (Prometheus, Loki retention)

## Deployment Decision Tree

Choosing the right deployment model depends on team capability, scale requirements, and observability depth. The decision tree below guides selection:

```
START: Deployment Path Selection
│
├─ Question 1: Is this local development for a contributor?
│  │
│  ├─ YES → Use **Aspire AppHost** (Development)
│  │        • Hot reload, live dashboards, full observability
│  │        • Prerequisite: .NET 8 SDK + Docker Desktop
│  │        • ETA to running: 5-10 minutes
│  │        • Best for: Feature development, debugging, architecture exploration
│  │
│  └─ NO → Continue to Question 2
│
├─ Question 2: Do you need Kubernetes or cloud-native scaling?
│  │
│  ├─ YES (Kubernetes) → Use **Kubernetes Deployment** (Future)
│  │        • Production multi-node orchestration
│  │        • Rolling updates, health checks, autoscaling
│  │        • Prerequisite: kubectl, Helm, cluster access
│  │        • ETA to running: 30-60 minutes (after images pushed to registry)
│  │        • Best for: Multi-region, high-availability, enterprise deployments
│  │
│  ├─ YES (Azure Serverless) → Use **Azure Container Apps** (Future)
│  │        • Managed serverless containers on Azure
│  │        • Automatic scaling, managed HTTPS, built-in observability
│  │        • Prerequisite: Azure subscription, az CLI
│  │        • ETA to running: 15-25 minutes (after images pushed to ACR)
│  │        • Best for: Small-to-medium self-hosted deployments, minimal ops
│  │
│  └─ NO → Continue to Question 3
│
├─ Question 3: Do you need to run on a single machine (self-hosted)?
│  │
│  └─ YES → Use **Docker Compose** (Single-host Production)
│           • Recommended: Single-machine deployments with persistent storage
│           • Prerequisite: Docker Engine, docker-compose CLI
│           • ETA to running: 15-25 minutes
│           • Best for: Self-hosters, small teams, CI/CD testing
│
└─ END: Deploy using selected topology
```

### Decision Factors

| Factor | Aspire Dev | Compose | Kubernetes | Azure Container Apps |
|--------|-----------|---------|-----------|----------------------|
| **Setup Time** | 5-10 min | 15-25 min | 30-60 min | 15-25 min |
| **Complexity** | Low | Medium | High | Medium |
| **Scalability** | Single host | Single host | Multi-node | Multi-region |
| **Observability** | Dashboard UI | Prometheus/Loki | Native | Azure Monitor |
| **Cost** | Free (dev) | Minimal (infra) | Medium-high | Pay-per-execution |
| **Infrastructure** | Local Docker | Docker + volumes | K8s cluster | Azure account |
| **Team Capability** | Developers | SREs/DevOps | Platform Eng | Cloud DevOps |

---

## Deployment Patterns: High-Level Strategies

### Pattern 1: Health Checking Strategy

Health checks allow orchestrators (Docker Compose, Kubernetes) to detect service failures and take corrective action.

**Streaming Digest Health Check Pattern:**

```
┌──────────────────────────────────────────────────────────┐
│                  Service Health Check Flow               │
├──────────────────────────────────────────────────────────┤
│                                                          │
│  1. Orchestrator periodically polls /health/live        │
│     (every 10 seconds, timeout 5 seconds)               │
│                                                          │
│  2. API service responds with probe results:            │
│     • Database connectivity (query SELECT 1)            │
│     • Ollama service reachability (HTTP HEAD)           │
│     • Whisper service availability (HTTP HEAD)          │
│     • Message queue connectivity (Hangfire state)       │
│     • Observability stack readiness (metrics export)    │
│                                                          │
│  3. Aggregated response:                                │
│     ├─ 200 OK + HealthStatus.Healthy                    │
│     │  → All dependencies operational                   │
│     │                                                   │
│     ├─ 200 OK + HealthStatus.Degraded                   │
│     │  → Core service running, some deps unavailable    │
│     │  → Orchestrator logs warning, continues serving   │
│     │                                                   │
│     └─ 503 Unavailable + HealthStatus.Unhealthy         │
│        → Critical dependency failed                     │
│        → Orchestrator removes from load balancer,       │
│           attempts restart, eventually removes          │
│                                                          │
│  4. Orchestrator action:                                │
│     • Healthy → Keep running, route traffic             │
│     • Degraded → Keep running, emit metric              │
│     • Unhealthy → Restart container, retry              │
│                  If persist: remove and alert           │
│                                                          │
└──────────────────────────────────────────────────────────┘
```

**Docker Compose Configuration:**

```yaml
services:
  api:
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health/live"]
      interval: 10s
      timeout: 5s
      retries: 3
      start_period: 30s
    restart_policy:
      condition: on-failure
      max_attempts: 5
      window: 120s
```

**Kubernetes Probe Configuration:**

```yaml
containers:
- name: api
  livenessProbe:
    httpGet:
      path: /health/live
      port: 8080
    initialDelaySeconds: 30
    periodSeconds: 10
    timeoutSeconds: 5
    failureThreshold: 3
  readinessProbe:
    httpGet:
      path: /health/ready
      port: 8080
    initialDelaySeconds: 10
    periodSeconds: 5
    timeoutSeconds: 3
    failureThreshold: 2
```

### Pattern 2: Zero-Downtime Deployment

Zero-downtime deployment ensures active requests complete gracefully before a service restarts. This pattern applies to Compose and Kubernetes deployments.

**Streaming Digest Zero-Downtime Process:**

```
Timeline (seconds)     Event
──────────────────────────────────────────────────────
T+0:   Orchestrator initiates rolling update
       │
       ├─ New pod/container created, waits for readiness
       │
T+5:   New instance passes readiness probe
       │
       ├─ Load balancer begins routing NEW requests to new instance
       ├─ OLD instance still receives in-flight requests (no new ones)
       │
T+10:  Graceful shutdown signal (SIGTERM) sent to old instance
       │
       ├─ Old instance stops accepting new connections
       ├─ Waits up to 30 seconds for active requests to complete
       ├─ Long-running ingestion jobs timeout after 5 minutes
       │  (orchestrator will eventually force-kill)
       │
T+40:  All requests drained from old instance, shutdown complete
       │
       ├─ Next old instance begins same process
       │
T+80:  Deployment complete, all instances updated
```

**Graceful Shutdown Configuration:**

- **Shutdown timeout:** 30 seconds (environment: `ASPNETCORE_SHUTDOWN_TIMEOUT=30`)
- **Background job timeout:** 5 minutes (for long-running ingestion)
- **Connection draining:** HTTP/1.1 keep-alive connections closed, no new requests accepted
- **Queue flushing:** Hangfire jobs finish in-progress work or queue for retry

**Docker Compose Shutdown Sequence:**

```yaml
services:
  api:
    stop_grace_period: 30s  # Wait 30s before SIGKILL
    environment:
      ASPNETCORE_SHUTDOWN_TIMEOUT: 30
```

### Pattern 3: Scaling Considerations

Streaming Digest scales along multiple dimensions:

**Horizontal Scaling (adding instances):**
- API tier: Stateless, scales 1→10+ (load balancer distributes)
- Worker tier: Scales 1→5+ (Hangfire distributes jobs)
- Database: Single PostgreSQL instance (vertical scale up to 32GB+ RAM)
- Ollama/Whisper: May need separate dedicated nodes (GPU-intensive)

**Vertical Scaling (increasing resources per instance):**
- Worker service benefits most from additional CPU (job parallelism)
- API service benefits from additional RAM (in-memory caching)
- PostgreSQL benefits from SSD storage + RAM (query performance)
- Ollama/Whisper critical on GPU availability

**Scalability Bottlenecks:**

| Component | Bottleneck | Solution |
|-----------|-----------|----------|
| **PostgreSQL** | Single-node throughput | Vertical scale + read replicas (future) |
| **Hangfire Queue** | Job processing latency | Increase worker instances/parallelism |
| **Ollama/Whisper** | Model inference time | GPU acceleration, larger models |
| **Network I/O** | Inter-service latency | Collocate services, use fast networks |
| **Observability** | Cardinality explosion | Limit high-cardinality labels, retention policies |

---

## Next Steps

See:
- **#262: Deployment Models Guide** — When to use each topology, target personas
- **#263: Compose Regeneration Workflow** — Step-by-step publish and update procedures
- **#264: Advanced Scenarios** — Multi-machine deployment, GPU acceleration, Kubernetes future
- **#265: Configuration Continuity** — Detailed secrets, config, and migration procedures
