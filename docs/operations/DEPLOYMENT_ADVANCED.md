# Advanced Deployment Scenarios

Status: Draft for advanced operators and infrastructure teams.

This guide extends the primary deployment guidance in:

- [docs/deployment/DEPLOYMENT_MODELS.md](../deployment/DEPLOYMENT_MODELS.md)
- [docs/ASPIRE_COMPOSE_WORKFLOW.md](../ASPIRE_COMPOSE_WORKFLOW.md)
- [docs/CONFIG_CONTINUITY.md](../CONFIG_CONTINUITY.md)
- [docs/operations/UPGRADE_PATHS.md](UPGRADE_PATHS.md)

Use it when you need to run Streaming Digest outside the default Compose-first or Aspire-local paths, or when you need to support hardened production deployments, multiple hosts, or hardware-specific inference services.

## Quick decision guide

| Scenario | Recommendation | Why |
|---|---|---|
| Single host, no orchestration | Docker Compose or bare Docker | Simplest operational model |
| Multiple servers, shared services | Multi-host Compose with private network + shared volumes | Minimal transition from single-host setup |
| Enterprise production | Kubernetes or Compose + reverse proxy + managed Postgres | Better scaling, policy, and lifecycle controls |
| Local GPU inference | Docker Compose with hardware passthrough | Most compatible with NVIDIA / Apple Metal setups |
| Private network / homelab | Tailscale + reverse proxy + TLS termination | Best security and convenience |
| Full automation / GitOps | Kubernetes or Swarm | Suitable for repeatable rollouts |

## 1. Bare Docker without Compose

Bare Docker is useful when:

- You want to avoid `docker compose` due to environment constraints.
- You need to run a custom startup or health-check sequence.
- You prefer to manage networking, env vars, and volumes explicitly.
- You are deploying a single-host instance but want full control over service wiring.

### When it makes sense

Choose bare Docker when:

- The deployment is on a single Linux host.
- You have a trusted operator comfortable with `docker run` and network aliases.
- You need a small custom deployment wrapper for private infra.
- You want to integrate with a VM, cluster node, or non-standard scheduling system.

### Recommended service topology

Run each core dependency as a separate container:

- `postgres` with `pgvector` enabled
- `ollama` for embeddings / optional LLM runtime
- `whisper` or a compatible inference service
- `api` for the backend
- `worker` for scheduled and ingestion jobs
- `web` for the UI
- optional `otel-collector`, `prometheus`, `grafana`, `loki`

Example network:

```bash
# Create a dedicated network
NETWORK_NAME=streaming-digest

docker network create ${NETWORK_NAME}
```

Then start each service with explicit `--network` and `--name` settings, for example:

```bash
# PostgreSQL

docker run -d \
  --name streaming-digest-postgres \
  --network ${NETWORK_NAME} \
  -e POSTGRES_DB=streaming_digest \
  -e POSTGRES_USER=postgres \
  -e POSTGRES_PASSWORD=CHANGE_ME \
  -v streaming-digest-pgdata:/var/lib/postgresql/data \
  pgvector/pgvector:pg17 \
  postgres -c shared_preload_libraries=pg_stat_statements

# API

docker run -d \
  --name streaming-digest-api \
  --network ${NETWORK_NAME} \
  -p 8000:8080 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e ConnectionStrings__DefaultConnection="Host=streaming-digest-postgres;Database=streaming_digest;Username=postgres;Password=CHANGE_ME" \
  -e OLLAMA_BASE_URL="http://streaming-digest-ollama:11434" \
  -v /var/lib/streaming-digest/config:/app/config:ro \
  ghcr.io/matthewcorven/streaming-digest-api:latest
```

### Bare Docker requirements

- A stable Docker bridge or user-defined network
- Explicit container health checks
- Persistent volume mounts for Postgres, model cache, and any runtime app data
- An env file or externalized config loader
- Startup ordering rules (`depends_on` is not sufficient for full readiness; use health checks and retries)

### Recommended health-check pattern

```bash
# Postgres readiness check
curl -fsS http://localhost:8000/health || exit 1
```

and for background services:

```bash
docker inspect --format='{{json .State.Health}}' streaming-digest-api
```

### Bare Docker limitations

- Manual dependency order and restarts are error-prone.
- Service discovery is less ergonomic than Compose or Kubernetes.
- No built-in stack-level rollback automation.
- You must handle TLS, reverse proxy, and storage lifecycle yourself.

## 2. Multi-machine Compose

Use multi-machine Compose when:

- The database runs on one host and the application runs on another.
- You want to separate ingestion/worker compute from web/API hosting.
- You need a private network across machines but do not yet need a full orchestrator.

### Common topology

| Host | Purpose |
|---|---|
| `db01` | PostgreSQL + pgvector + backups |
| `app01` | API + web + worker |
| `model01` | Ollama / Whisper / optional GPU node |
| `proxy01` | Nginx/Caddy + TLS termination |

### Networking model

Use:

- a private VPN or overlay network between hosts
- fixed private IPs or Tailscale network names
- hostnames instead of localhost for inter-service communication

Example service wiring:

```yaml
services:
  api:
    environment:
      ConnectionStrings__DefaultConnection: "Host=db01.internal;Database=streaming_digest;Username=postgres;Password=${POSTGRES_PASSWORD}"
      OLLAMA_BASE_URL: "http://model01.internal:11434"
      WEBSITE_URL: "https://digest.example.com"
```

### Multi-host operational guidance

- Keep the database on the most stable machine.
- Use a named Docker network with proper host reachability.
- Keep volumes local to the host that owns the service.
- Use backup jobs from the database host, not from the web host.
- Place TLS termination at the reverse proxy, not inside app containers.

### Shared storage and state

When jobs or model data are split across hosts:

- define one authoritative storage owner for the database
- do not mount a single volume across two machines unless the FS supports it reliably
- prefer networked storage (NFS/SMB/longhorn/Gluster) only for durable shared content, not for high-churn database state

### Multi-machine safety rules

- Never assume a service is ready just because the container started.
- Use service health checks and write a boot order checklist.
- Keep `POSTGRES_PASSWORD`, `JWT_SECRET`, and model credentials in a secure secret source.
- If the model service is remote, make sure the API and worker are configured for remote inference latency and failure-handling.

## 3. Docker Swarm vs Docker Compose

The Compose-first workflow is the default MVP path. Swarm is useful when you need a more cluster-aware deployment surface without adopting a full Kubernetes control plane.

### Docker Compose

Good fit for:

- single-host or small multi-host deployments
- simple rollback and compose templating
- local self-hosting and homelab needs
- teams that want low operational overhead

### Docker Swarm

Good fit for:

- multiple nodes with a need for service replication and rolling updates
- service-level orchestration without Kubernetes complexity
- environments where existing Docker Enterprise or Admin-managed clusters are in place

### Swarm trade-offs

| Dimension | Compose | Swarm |
|---|---|---|
| Ease of use | High | Medium |
| Multi-node orchestration | Low | High |
| Rolling updates | Manual or script-driven | Built-in |
| Service health / restart policy | Good | Good |
| Networking model | Simpler | More opinionated |
| Learning curve | Low | Medium |
| Best for | Small deployments | Clustered ops teams |

### Swarm guidance for Streaming Digest

- Keep database state on a single node or use managed Postgres if you need production resilience.
- Run the API/web stack as replicated services if load warrants it.
- Treat Ollama and Whisper as specialized model services; avoid scaling them blindly.
- Prefer `docker stack deploy` only after the Compose artifact has been validated on a single host.

### Recommended rollout pattern

1. Validate the standard Compose stack.
2. Move to a single-host Swarm test deployment.
3. Separate stateful and stateless services.
4. Keep a clear backup and restore plan before adoption.

## 4. Kubernetes MVP design

Kubernetes is the right long-term production answer when you have a team operating the platform and want reproducible infrastructure semantics. For MVP, keep the design simple and stateful-service aware.

### Scope for MVP

Use Kubernetes only for service orchestration, not for experimentation with every feature. Keep the topology straightforward:

- `streaming-digest` namespace
- one Postgres StatefulSet with persistent volume claim
- API and web Deployments
- worker Deployment
- Ollama Deployment with GPU or CPU configuration
- reverse proxy / ingress deployment with TLS termination
- optional observability stack via Prometheus/Grafana/Tempo

### Minimal architecture

```text
Ingress / Load Balancer
        │
        ▼
   streaming-digest-web
   streaming-digest-api
        │
        ▼
   streaming-digest-worker
        │
        ├── Postgres StatefulSet
        ├── Ollama Deployment
        └── Whisper Deployment (optional)
```

### Important design choices

- Keep Postgres out of the API container and use a dedicated StatefulSet.
- Use separate PVCs for database, model cache, and temp storage.
- Put ingress and TLS termination outside the app containers.
- Use Kubernetes `ReadinessProbe` and `LivenessProbe` for API and worker startup safety.
- For model workloads, prefer Node selectors or taints/tolerations for GPU-enabled nodes.

### Example `kind` of manifest guidance

```yaml
apiVersion: v1
kind: Service
metadata:
  name: streaming-digest-api
spec:
  selector:
    app: streaming-digest-api
  ports:
    - port: 8080
      targetPort: 8080
```

This is intentionally simple; the full production topology should evolve from a verified Compose baseline rather than being invented from scratch.

### Kubernetes limitations to account for

- Model services are not “normal” stateless apps; they often need more memory and GPU affinity.
- Postgres storage needs durable provisioning and operational backups.
- You do not want to copy the entire app stack into a complex microservice architecture unless team maturity is high.

## 5. GPU and hardware-specific deployment

### NVIDIA GPU

For CUDA-backed Docker deployments:

```bash
docker run --gpus all \
  -e NVIDIA_VISIBLE_DEVICES=all \
  -e NVIDIA_DRIVER_CAPABILITIES=compute,utility \
  ...
```

With Compose:

```yaml
deploy:
  resources:
    reservations:
      devices:
        - capabilities: ["gpu"]
```

Keep the following in mind:

- GPU memory availability can limit embedding and LLM model loading.
- Mixed CPU/GPU mode should be explicit and documented.
- Your model runtime should have a fallback path when GPU is unavailable.

### Apple Metal / ARM inference

If the environment is Apple Silicon or other ARM hardware:

- prefer model runtimes and images that support ARM64
- test the model and container combination before production deployment
- verify that loaded models match the expected vector dimensions and runtime assumptions

### CPU-only mode

Always keep a CPU fallback documented:

- for small models and development builds
- for recovery when GPU drivers are unavailable
- for constrained edge or home-server setups

### Hardware recommendations

| Use case | Recommended baseline |
|---|---|
| Single-user local dev | 8-16 GB RAM, CPU-only acceptable |
| Self-hosted knowledge system | 16-32 GB RAM, optional GPU |
| Production inference | 32+ GB RAM, dedicated GPU or highly tuned CPU pool |
| Multi-instance / multi-tenant | Separate model workloads from app workloads |

## 6. Network security patterns

### Private network / VPN

Recommended when:

- you want to expose only a subset of services externally
- you run on a homelab or remote server behind NAT
- you need to separate app traffic from public internet exposure

Common choices:

- Tailscale
- WireGuard
- ZeroTier
- site-to-site VPN between hosts

### Reverse proxy and layer 7 routing

Use a reverse proxy for:

- TLS termination
- access control
- request limiting
- path-based routing
- simple upstream health checks

Example patterns:

- `https://digest.example.com` -> API or web app
- `https://digest.example.com/admin` -> internal admin plane
- `https://grafana.example.com` -> dashboard endpoint

Recommended proxy options:

- Caddy for simpler automatic TLS
- Nginx for traditional static config and operator familiarity
- Traefik for dynamic routing and automatic service discovery

### Firewall and service exposure

Expose only what you need:

- keep Postgres off public networks unless intentionally exposed
- restrict model inference ports to trusted internal subnets
- limit administrative UIs to LAN or VPN access
- keep observability endpoints behind auth or VPN

### Security best practices

- Do not expose debug endpoints or admin tools publicly.
- Keep environment variables and secrets out of Git.
- Prefer managed secret stores or Docker secrets when possible.
- Rotate credentials on every deployment change that introduces a new host or user.

## 7. TLS and certificate management

### Recommended patterns

1. Managed certificates via a reverse proxy and ACME automation.
2. Self-signed certificates for private networks or lab deployments.
3. Internal CA for a trusted homelab environment.

### Caddy example

```caddyfile
example.com {
  reverse_proxy api:8080
  tls internal
}
```

### Nginx example

```nginx
server {
    listen 443 ssl;
    server_name digest.example.com;

    ssl_certificate /etc/letsencrypt/live/digest.example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/digest.example.com/privkey.pem;

    location / {
        proxy_pass http://api:8080;
    }
}
```

### Safe TLS guidance

- Keep TLS offload at the edge, not inside the app unless necessary.
- Use strict HTTPS redirect rules.
- Verify certificate renewal and expiry monitoring.
- Provide a documented fallback for private-network/self-signed certs.

## 8. Storage and volume strategy

### State by type

| Data type | Recommendation | Notes |
|---|---|---|
| Database | Dedicated Postgres volume | High durability, critical state |
| Model cache | Dedicated volume or host path | Large, expensive to rebuild |
| Temp files | Separate temp volume | Avoids filling app root |
| Screenshots / extracted assets | Dedicated storage volume | Keep growth clearly bounded |
| Logs | Container logging or dedicated log volume | Rotate aggressively |
| Backups | Separate backup volume or network share | Never keep backups embedded in app runtime volumes |

### Volume options

- Local Docker volumes for a single-host deployment
- Bind mounts for explicit host paths in trusted environments
- NFS / SMB / network-attached storage for multi-host deployments
- cloud storage for multi-node or managed clusters

### Local, NFS, and cloud guidance

#### Local Docker volumes
Best for:

- single-host dev or homelab installs
- predictable usage and low complexity

#### NFS / SMB
Best for:

- multi-host but small-footprint clusters
- storage that must outlive a single node

#### Cloud storage
Best for:

- managed production and HA environments
- cross-region or backup-focused operations

### Volume safety rules

- Do not silently switch volume paths during upgrades.
- Keep a backup before any volume migration.
- Validate disk permissions before enabling a new mount.
- Keep retention policy explicit for model cache, logs, and backups.

## 9. Backup and restore by deployment model

### 9.1 Single-host Compose backup

```bash
# Backup Postgres
docker compose exec postgres pg_dump -U postgres streaming_digest > ./backups/streaming-digest-$(date +%F).sql

# Backup configuration
cp .env ./backups/.env.$(date +%F)
cp compose.yaml ./backups/compose.$(date +%F).yaml
```

### 9.2 Multi-host Compose backup

- Back up the database from the database host.
- Back up env config from the deployment repo or secrets store.
- Snapshot any shared volumes independently.
- Validate backup integrity before deleting old storage.

### 9.3 Kubernetes backup

- Use a PostgreSQL backup job or database snapshot tooling.
- Snapshot persistent volumes for stateful data.
- Back up ingress / secret manifests and app config separately.
- Keep disaster-recovery runbooks distinct from day-to-day upgrades.

### 9.4 Restore checklist

1. Restore database from backup.
2. Restore config and secrets.
3. Recreate or remount storage volumes.
4. Validate service connectivity.
5. Run startup health checks.
6. Rebuild or verify model cache if needed.
7. Resume scheduled jobs only after dependency health is confirmed.

## 10. Recommended operator runbook

Use the following order when making a deployment change:

1. Confirm deployment class (Compose, bare Docker, Swarm, Kubernetes).
2. Validate config continuity and application versions.
3. Backup database and secrets.
4. Update the runtime topology or manifests.
5. Validate service health and readiness before resuming ingestion.
6. Roll back only if the app fails preflight checks.

This keeps the app safe while avoiding unplanned capacity or data loss during infrastructure changes.

## 11. Recommended path by operating maturity

| Maturity | Recommended path |
|---|---|
| Personal / home lab | Docker Compose |
| Small team / edge production | Compose + reverse proxy + private network |
| Enterprise ops | Kubernetes or Swarm |
| Research / experiments | Bare Docker or single-host Compose with explicit custom config |

## 12. Related guidance

- [Deploying Models and Roles](../deployment/DEPLOYMENT_MODELS.md)
- [AppHost → Compose Workflow](../ASPIRE_COMPOSE_WORKFLOW.md)
- [Configuration Continuity](../CONFIG_CONTINUITY.md)
- [Upgrade Paths](UPGRADE_PATHS.md)

## 13. Acceptance checklist

This guide covers the required advanced scenarios:

- [x] Bare Docker without Compose
- [x] Multi-machine Compose
- [x] Docker Swarm comparison and setup guidance
- [x] Kubernetes MVP architecture and constraints
- [x] GPU and hardware specialization
- [x] Network security and reverse proxy patterns
- [x] TLS and certificate management
- [x] Volume and storage strategy
- [x] Backup / restore procedures across deployment models
