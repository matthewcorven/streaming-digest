# Developer Onboarding Guide

This guide is for developers and OSS contributors working on Streaming Digest.

## Prerequisites

- **.NET 10 SDK** or later — https://dotnet.microsoft.com/download
- **Docker** and **Docker Compose** (v2.20+)
- **Git**
- **Code editor** — VS Code, JetBrains Rider, or Visual Studio
- **Bash/Zsh** (for running scripts)

On macOS, you can install everything via Homebrew:

```bash
brew install dotnet docker git
brew install --cask docker
```

## Development setup

### 1. Clone and restore

```bash
git clone https://github.com/matthewcorven/streaming-digest.git
cd streaming-digest
dotnet restore
```

### 2. Build and verify

```bash
dotnet build
```

This builds the entire solution including test projects. If you see any errors, check your .NET SDK version:

```bash
dotnet --version
```

Should be 10.0.0 or later.

### 3. Start the Aspire development stack

```bash
dotnet run --project src/StreamingDigest.AppHost
```

This starts:
- **Aspire Dashboard** at http://localhost:18888 (automatically opens in browser)
- **All services** (API, Worker, PostgreSQL, Ollama, Whisper, etc.)
- **OpenTelemetry collection** for local observability

The Aspire dashboard lets you:
- Monitor resource health and logs in real-time
- Restart individual services
- View metrics and traces
- Access service endpoints

### 4. Verify services are running

In the Aspire dashboard, you should see:

```
✓ streaming-digest-api           http://localhost:8080
✓ streaming-digest-postgres       (running)
✓ streaming-digest-ollama         (running)
✓ streaming-digest-whisper        (running)
✓ streaming-digest-scraper        (running)
✓ streaming-digest-worker         (running)
✓ ...observability services...
```

All should show "✓ Running" or "✓ Healthy".

### 5. Open the web app

Navigate to http://localhost:8080

Complete first-run setup:
1. Create your user account
2. Download embedding model (`bge-m3`)
3. Verify Whisper is running

## Project structure

```
streaming-digest/
├── src/
│   ├── StreamingDigest.Api/              # ASP.NET Core API + Blazor WASM UI
│   ├── StreamingDigest.Worker/           # Hangfire background jobs
│   ├── StreamingDigest.Infrastructure/   # Data access, services, domain logic
│   ├── StreamingDigest.Domain/           # Domain entities, interfaces
│   ├── StreamingDigest.AppHost/          # Aspire AppHost (local dev + deployment)
│   ├── StreamingDigest.ServiceDefaults/  # Aspire service configuration
│   ├── StreamingDigest.Scraper/          # Crawlee/Playwright scraper service
│   └── StreamingDigest.MatrixNotifier/   # Matrix notification service
├── docs/
│   ├── product/                          # PRD, requirements
│   ├── architecture/                     # Architecture, data model
│   ├── api/                              # API specification
│   ├── adr/                              # Architectural decision records
│   ├── operations/                       # Deployment, backup, troubleshooting
│   └── verification/                     # Performance baselines, test results
├── tests/
│   ├── StreamingDigest.*.Tests/          # Unit and integration tests
│   └── Fixtures/                         # Test data
├── scripts/
│   └── publish_compose.sh                # Regenerate compose.yaml from AppHost
├── compose.yaml                          # Docker Compose (generated from AppHost)
├── Dockerfile.whisper                    # Whisper service image
├── CONTEXT.md                            # Project context and conventions
├── ONBOARDING.md                         # This file (feature overview)
└── README.md                             # Main readme
```

## Code structure by domain

### API and UI

**`src/StreamingDigest.Api/`**
- ASP.NET Core 10 REST API
- Hosted Blazor WASM application
- Authentication/authorization
- Health checks
- Hangfire admin dashboard at `/admin/jobs`

**`src/StreamingDigest.Api/Components/`**
- Blazor WASM components using Fluent UI components
- Pages: Search, Channels, Ingestion, Settings, Admin
- Client-side state management via C# async methods + browser storage

### Background processing

**`src/StreamingDigest.Worker/`**
- Hangfire recurring job scheduler
- Calls into `Infrastructure` services for ingestion logic
- Runs on startup and schedules the daily `ingestion.scheduled` job

**`src/StreamingDigest.Infrastructure/Jobs/`**
- Ingestion job orchestration
- Video processing pipeline
- Parallel task execution with rate limiting

### Domain models and services

**`src/StreamingDigest.Domain/`**
- Entities (Video, Segment, Link, Repository, Note, etc.)
- Value objects (ChannelId, VideoId, etc.)
- Interfaces (IEmbeddingService, IYouTubeAdapter, etc.)
- Domain events

**`src/StreamingDigest.Infrastructure/`**
- Data access (EF Core DbContext, Dapper queries)
- External service adapters (YouTube, Ollama, Whisper, etc.)
- Embedded vector search
- Screenshot storage
- Matrix notifications

### External services

**`src/StreamingDigest.Scraper/`**
- Node.js + Crawlee/Playwright
- Hosted as a separate Docker service
- REST API for webpage scraping

**`src/StreamingDigest.MatrixNotifier/`**
- Node.js Matrix bot client
- Listens for notification events via HTTP
- Sends messages to configured rooms

## Development workflow

### Creating a feature

1. **Create an issue** describing the work (or pick one from the backlog)
2. **Create a branch** from `main`:
   ```bash
   git checkout -b feature/descriptive-name
   ```
3. **Implement the feature**:
   - Write the code
   - Add tests
   - Update API docs if needed
   - Update `.md` files if needed
4. **Test locally**:
   - Run tests: `dotnet test`
   - Run the app: `dotnet run --project src/StreamingDigest.AppHost`
   - Test the feature in the web UI
5. **Commit and push**:
   ```bash
   git add .
   git commit -m "feat: descriptive message"
   git push origin feature/descriptive-name
   ```
6. **Create a pull request** with a clear description
7. **Address review feedback** and iterate
8. **Merge** when approved

### Running tests

Run all tests:
```bash
dotnet test
```

Run tests for a specific project:
```bash
dotnet test tests/StreamingDigest.Infrastructure.Tests/
```

Run a specific test:
```bash
dotnet test --filter "MethodName"
```

### Debugging

**In VS Code:**
1. Install the C# extension (or use Omnisharp)
2. Set breakpoints in the editor
3. Press F5 to launch the debugger
4. Breakpoints will pause execution

**In Rider:**
1. Set breakpoints
2. Click Debug in the gutter or press Shift+F9

**In Visual Studio:**
1. Set breakpoints
2. Press F5 to start debugging

### Database migrations

Streaming Digest uses EF Core with code-first migrations.

To create a new migration after changing entities:

```bash
dotnet ef migrations add MigrationName \
  --project src/StreamingDigest.Infrastructure \
  --startup-project src/StreamingDigest.Api
```

This creates a new migration file in `src/StreamingDigest.Infrastructure/Migrations/`.

Migrations run automatically on app startup.

To revert a migration (development only):

```bash
dotnet ef database update PreviousMigrationName \
  --project src/StreamingDigest.Infrastructure \
  --startup-project src/StreamingDigest.Api
```

## Architecture decisions

Key architectural patterns are documented in `docs/adr/`:

- **ADR-0001**: Staleness is derived, not stored
- **ADR-0005**: Runs are immutable; operations are handles
- **ADR-0006**: Digest is stored per ingestion run
- **ADR-0009**: Repository owns metadata; resource delegates
- **ADR-0010**: Fixed transcript preference with cutover
- Plus 7 more ADRs covering embeddings, segmentation, scale, and index strategy

Read these before making significant design changes.

## Updating the Docker Compose setup

Streaming Digest uses **Aspire** to define the local development and deployment stack.

The `compose.yaml` file is **generated** from the Aspire AppHost and should NOT be hand-edited.

**When to regenerate `compose.yaml`:**

After you make any of these changes to `src/StreamingDigest.AppHost/Program.cs`:
- Add/remove a service
- Change service port mappings
- Change environment variables
- Modify resource configuration
- Add/remove healthchecks
- Change resource dependencies

**To regenerate:**

```bash
./scripts/publish_compose.sh
```

This script:
1. Runs `aspire publish` with container output format
2. Replaces the repo-root `compose.yaml` with the generated version
3. Commits the change (if desired)

**Do not hand-edit `compose.yaml`.** Always regenerate after AppHost changes. This ensures development (Aspire) and deployment (Compose) stay aligned.

## Zero-intervention onboarding

Streaming Digest is designed for zero-intervention onboarding — fresh `docker compose up -d` should start all services cleanly.

When adding features, ensure:

1. **New services have healthchecks** — expose `/health` endpoint and define Docker `healthcheck` in the image or compose config
2. **Dependencies are explicit** — use `depends_on: { service_name: { condition: service_healthy } }` in compose
3. **Graceful degradation** — optional services must not block startup
4. **Safe `.env.example` defaults** — add environment variables with production-ready local defaults
5. **No post-startup manual steps** — first `docker compose up -d` should reach a usable state

**Test zero-intervention:**

```bash
docker compose down -v
docker compose up -d
docker compose ps  # all services should be healthy
```

See [ONBOARDING.md](../../ONBOARDING.md) for details.

## Observability during development

### Aspire Dashboard

The Aspire dashboard (http://localhost:18888) shows:
- Resource logs in real-time
- Environment variables and resource details
- Endpoint URLs
- Health status

Click on any service to drill into logs, restart, or view configuration.

### OpenTelemetry Collector

Traces and metrics flow to OTel Collector, which forwards to:
- **Tempo** (http://localhost:3200) — distributed traces
- **Prometheus** (http://localhost:9090) — metrics
- **Loki** (http://localhost:3100) — logs

### Application logging

Logs are emitted to console and structured logs (OpenTelemetry). Use the Aspire dashboard or Loki to search.

Common log levels:
- `Information` — normal operation
- `Warning` — recoverable issues (rate limit, retry)
- `Error` — exceptions or failures
- `Debug` — detailed tracing (only in development)

## Contributing guidelines

### Code style

- Use **C# 13 features** (nullable reference types, records, top-level statements)
- Follow **Microsoft naming conventions** (PascalCase for public members)
- Use **async/await** for I/O operations
- Add **XML comments** to public APIs
- Keep methods small and focused

### Commit messages

Follow conventional commits:
```
feat: add user note editing
fix: resolve null reference in search
docs: update deployment guide
test: add integration tests for scraper
refactor: simplify embedding pipeline
```

### Pull request titles

Use clear, present-tense titles:
- ✅ "Add search result explanation"
- ✅ "Fix database connection pooling"
- ✅ "Update developer documentation"
- ❌ "Update stuff"
- ❌ "WIP"

### Review expectations

- Code review is required before merge
- Tests must pass: `dotnet test`
- No breaking changes without discussion
- Documentation must be updated for user-facing changes

## Resources

- **[Architecture](../../docs/architecture/ARCHITECTURE.md)** — system design and component responsibilities
- **[API Specification](../../docs/api/API_SPEC.md)** — endpoint details and request/response schemas
- **[Data Model](../../docs/architecture/DATA_MODEL.md)** — entity relationships and schema
- **[Product PRD](../../docs/product/PRD.md)** — feature requirements and roadmap
- **[Architectural Decisions](../../docs/adr/)** — design rationale for key patterns
- **[Issue Tracker](https://github.com/matthewcorven/streaming-digest/issues)** — current work and roadmap
- **[CONTEXT.md](../../CONTEXT.md)** — project conventions and standards

## Getting unstuck

**My build fails:**
- Run `dotnet clean && dotnet restore && dotnet build`
- Check your .NET SDK version: `dotnet --version`
- Check .NET workloads: `dotnet workload list`

**Tests are failing:**
- Run `dotnet test --verbosity detailed` for more info
- Check test logs in the output
- Run a specific test to isolate: `dotnet test --filter "NameOfTest"`

**Services won't start:**
- Check Aspire dashboard for logs and errors
- Verify ports are available: `lsof -i :8080` (macOS/Linux)
- Check Docker is running: `docker ps`

**Database is corrupted:**
- Delete the PostgreSQL volume: `docker volume ls | grep postgres` then `docker volume rm <name>`
- Restart: `dotnet run --project src/StreamingDigest.AppHost`

**Questions?**

Open an issue or discussion on GitHub, or email the team.

---

**Last updated:** 2026-08-11  
**Quick links:**  
[Main README](../../README.md) | [User Onboarding](./ONBOARDING_USERS.md) | [Architecture](../../docs/architecture/ARCHITECTURE.md) | [Contributing](../../.github/CONTRIBUTING.md) (if exists)
