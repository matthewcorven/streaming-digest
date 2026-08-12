# User Onboarding Guide

This guide is for users deploying Streaming Digest on their own infrastructure.

## System requirements

- **Docker Desktop** for Mac (v4.20+) with sufficient resource allocation
- **Mac hardware**: Apple Silicon (M1 Pro or higher recommended)
  - Minimum: M1 with 8 GB unified memory
  - Recommended: M3+ with 16+ GB unified memory for local LLM inference
  - For external inference: M1 with 8 GB minimum is sufficient
- **CPU**: 4+ cores allocated to Docker (default: host core count)
- **RAM**: 8 GB unified memory minimum; 16+ GB if running local LLM inference
- **Disk**: 50+ GB available (PostgreSQL + screenshots + models + telemetry)
- **Network**: Private (Tailscale, VPN, or internal network only)

## LLM/SLM hosting and runtime requirements

Streaming Digest uses LLMs/SLMs for text summarization during feed processing. Your choice of hosting strategy determines infrastructure requirements and performance.

### Hosting options (Mac-focused)

| Strategy | Setup | Resource cost | Performance | Use case |
|----------|-------|------|-------------|----------|
| **Local Ollama (Docker)** | Runs in Docker on Mac via Ollama container | 6–12 GB unified memory | Moderate (~30–120s per feed) on M-series GPU | Private, no external APIs, full control |
| **External Ollama** | Run Ollama on separate Mac/Linux with GPU, Streaming Digest connects via network | Minimal on Streaming Digest Mac; significant on inference Mac | Fast (depends on remote GPU) | Dedicated inference server, scales easily |
| **Cloud LLM API** | OpenAI, Anthropic, Groq, or compatible API | Pay-per-use ($0.01–$0.50 per 1K tokens) | Very fast (sub-second) | Simplest setup, outsourced inference |
| **Mixed** | Local SLM for speed + cloud LLM for quality | Moderate unified memory + API costs | Best of both: filter with local, enhance with cloud | Balance cost and quality |

### Local Ollama on Mac (recommended for privacy)

**Hardware requirements by Mac model:**

| Mac Model | Unified Memory | Ollama Performance | Recommended models |
|-----------|---|---|---|
| M1/M1 Pro | 8 GB | Slow (~120s per feed) | Phi-3 (3B), Llama 2 7B |
| M1 Max | 16 GB | Moderate (~60s per feed) | Llama 2 7B–13B, Mistral 7B |
| M2/M2 Pro | 8–16 GB | Moderate (~60–120s per feed) | Phi-3, Llama 2 7B–13B |
| M2 Max | 16–32 GB | Good (~30–60s per feed) | Llama 3.1 8B, Neural Chat 13B |
| M3/M3 Pro | 8–18 GB | Good (~30–60s per feed) | Llama 3.1 8B–13B |
| M3 Max | 24–36 GB | Excellent (~10–30s per feed) | Llama 3.1 70B (with quantization) |
| M4/M4 Pro | 12–20 GB | Excellent (~20–45s per feed) | Llama 3.1 8B–13B, best-in-class quality |
| M4 Max | 32–48 GB | Outstanding (~10–20s per feed) | Full model range |

**Apple Silicon performance baseline:**
- Phi-3 (3B model): ~10–15 seconds per feed (M1 Pro+)
- Llama 3.1 8B: ~30–90 seconds per feed (M2 Max+)
- Neural Chat 13B: ~60–180 seconds per feed (M3 Max+)
- Test feed summarization: `docker compose logs streaming-digest-api | grep "summarize"`

**Storage per model:**
- Phi-3 Mini (3B): 2 GB
- Llama 2 7B: 4 GB
- Llama 3.1 8B: 5 GB
- Neural Chat 13B: 8 GB
- Llama 3.1 70B (quantized): 40 GB

**Configuration in `.env`:**
```bash
# Use built-in Ollama container
STREAMINGDIGEST_LLM_PROVIDER=ollama
STREAMINGDIGEST_LLM_ENDPOINT=http://streaming-digest-ollama:11434

# Model selection (see table above for your Mac)
# M1/M1 Pro: use Phi-3 or smaller
# M2–M3 Pro: use Llama 3.1 8B
# M3 Max+: use Llama 3.1 13B or larger
STREAMINGDIGEST_LLM_MODEL=llama3.1:8b-instruct-q4_0
```

**Docker Desktop memory allocation:**
Ollama inference can consume significant unified memory. In Docker Desktop > Settings > Resources:
- Base memory: Set to 75% of your Mac's total (e.g., 12 GB on 16 GB Mac, 24 GB on 32 GB Mac)
- CPUs: Leave at default (uses all available)
- Swap: Increase if you see slowdowns with large models

**First-run model download:**
When Streaming Digest starts with a new `LLM_MODEL`, Ollama automatically pulls the model (~10–30 minutes first time, depending on size and network). Monitor progress:
```bash
docker compose logs streaming-digest-ollama
```

### External Ollama (on separate Mac or Linux machine)

Share a single Ollama server across multiple Streaming Digest instances:

**On the Ollama host Mac/Linux:**
```bash
# Install Ollama: ollama.ai (Mac) or https://github.com/ollama/ollama (Linux)
# Pull models
ollama pull llama3.1:8b-instruct-q4_0

# Start server accessible over network
OLLAMA_HOST=0.0.0.0:11434 ollama serve
```

**In Streaming Digest `.env`:**
```bash
STREAMINGDIGEST_LLM_PROVIDER=ollama
STREAMINGDIGEST_LLM_ENDPOINT=http://<ollama-host-ip>:11434
STREAMINGDIGEST_LLM_MODEL=llama3.1:8b-instruct-q4_0
```

**Benefits:**
- Streaming Digest host can run on smaller Mac (M1 8GB)
- Inference server can be on more powerful Mac (M3 Max) or dedicated Linux GPU machine
- Multiple Streaming Digest instances share one Ollama server
- Easier to upgrade inference hardware without touching Streaming Digest

**Network setup:**
- Ensure Ollama host and Streaming Digest host are on same private network (Tailscale, VPN, or LAN)
- Verify connectivity: `curl http://<ollama-host-ip>:11434/api/tags`

### Cloud LLM API (simplest setup)

Use OpenAI, Anthropic, Groq, or compatible APIs instead of local inference:

**Configuration in `.env`:**
```bash
# OpenAI
STREAMINGDIGEST_LLM_PROVIDER=openai
STREAMINGDIGEST_LLM_ENDPOINT=https://api.openai.com/v1
STREAMINGDIGEST_LLM_MODEL=gpt-4-mini
STREAMINGDIGEST_LLM_API_KEY=sk-...

# Or Groq (faster + cheaper):
STREAMINGDIGEST_LLM_PROVIDER=openai
STREAMINGDIGEST_LLM_ENDPOINT=https://api.groq.com/openai/v1
STREAMINGDIGEST_LLM_MODEL=mixtral-8x7b-32768
STREAMINGDIGEST_LLM_API_KEY=gsk-...
```

**Cost estimates** (2026 pricing):
- GPT-4 Mini: ~$0.05/1M input tokens, $0.15/1M output tokens
- Groq Mixtral: ~$0.24/1M tokens (input + output)
- Claude 3.5 Haiku: ~$0.80/$4.00 per 1M input/output tokens

**Typical costs for 20 feeds/day:**
- ~1.2M tokens/month → ~$0.50–$5.00/month depending on provider

**Benefits:**
- No local inference overhead; M1 Mac with 8 GB unified memory is sufficient
- Access to latest models and highest quality
- Highly available; no local tuning needed
- Predictable per-token costs

### Model selection guide

| Goal | Recommended | Mac requirements | Cost |
|------|-------------|------------------|------|
| **Lowest cost** | Phi-3 (local) | M1+ with 8 GB | Free (local) |
| **Best quality** | GPT-4 or Claude 3.5 | M1+ with 8 GB | $1–5/month |
| **Balanced** | Llama 3.1 8B (local) | M2+ with 16 GB | Free (local) |
| **Maximum privacy** | Llama 3.1 (local) | M2 Max+ | Free (local) |
| **Fastest inference** | Groq API | M1+ with 8 GB | ~$2–5/month |

### Troubleshooting Ollama performance on Mac

**Slowdowns or OOM errors:**
1. Check Docker Desktop memory allocation: Settings > Resources > Base Memory
2. Monitor memory during summarization: `top -l1 | head -20`
3. Reduce model size (switch to Phi-3 or smaller 7B model)
4. Reduce Docker CPU allocation if thermal throttling occurs

**Model won't download:**
- Check network: `ping ollama.ai`
- Check disk space: `df -h` (need at least model size + 2 GB free)
- Check Docker memory: increase in Docker Desktop settings

**Connection refused errors:**
- Verify Ollama service is running: `docker compose ps | grep ollama`
- Check logs: `docker compose logs streaming-digest-ollama`
- Restart: `docker compose restart streaming-digest-ollama`

## Pre-deployment setup

### 1. Prepare your deployment directory

```bash
# Clone the repository
git clone https://github.com/matthewcorven/streaming-digest.git
cd streaming-digest

# Create your environment file from the template
cp .env.example .env
```

### 2. Review and customize `.env` (optional)

The defaults in `.env.example` are production-ready for local, single-user deployment. You only need to customize if:

- **Matrix notifications** — add bot credentials if you want alerts
- **PostgreSQL credentials** — change from defaults (optional but recommended for production)
- **Ollama models** — specify exact models to download (defaults are good)

**Do NOT change these for initial deployment:**
- `ASPNETCORE_ENVIRONMENT=Production`
- `NOTIFICATIONS_MATRIX_ENABLED=false` (Matrix is optional)
- `DATABASE_*` (local PostgreSQL works with defaults)

### 3. Pre-pull Docker images (optional but recommended)

This can speed up first startup:

```bash
docker compose pull
```

## Deployment

### Start the stack

```bash
docker compose up -d
```

This command:
1. Creates and starts all containers in the background
2. Waits for PostgreSQL to be ready
3. Initializes the database schema automatically
4. Starts API, Whisper, Scraper, Ollama, and observability services
5. All critical services reach "healthy" state within ~60 seconds

### Verify all services are healthy

```bash
docker compose ps
```

You should see:

```
NAME                            STATUS
streaming-digest-api           healthy (or running)
streaming-digest-postgres      healthy
streaming-digest-ollama        healthy
streaming-digest-whisper       healthy
streaming-digest-scraper       healthy
streaming-digest-prometheus    running
streaming-digest-grafana       running
streaming-digest-loki          running
streaming-digest-tempo         running
streaming-digest-otel-collector running
```

All `healthy` and `running` statuses indicate success.

### Check logs if a service isn't healthy

```bash
# Check API logs
docker compose logs streaming-digest-api

# Check Worker logs (runs background jobs)
docker compose logs streaming-digest-worker

# Check PostgreSQL logs
docker compose logs streaming-digest-postgres

# Check Whisper logs
docker compose logs streaming-digest-whisper
```

**Note:** The worker may show "database 'streamingdigest' does not exist" errors initially — this is expected (degraded mode) and resolves on first API request.

## First-run setup

Open http://localhost:8080 in your browser.

### 1. Create your user account

On first startup, you'll see a setup page (`/setup`).

1. Choose your username and password
2. Click "Create Account"
3. You're now logged in

**Security note:** This is your only user account. Streaming Digest is single-user only.

### 2. Configure AI models (optional but recommended)

Go to **Settings** → **Models**.

**Embedding model** (required for search):
- `bge-m3` is the default and recommended
- Click "Download" to fetch from Ollama (takes 1-2 minutes on first pull)
- Once downloaded, click "Verify" to confirm it's ready

**LLM model** (optional, used for segmentation):
- `llama3.1:8b` or `qwen2.5:7b` are recommended
- Download and verify one if you want automatic segment titles
- These are larger (2-8 GB), so download is slower

**Audio-to-text** (optional, defaults to Whisper):
- The Whisper service is started automatically
- It's used when videos don't have captions
- No configuration needed; just verify it shows "Running"

### 3. Configure Matrix notifications (optional)

If you want alerts when ingestion completes:

Go to **Settings** → **Notifications** → **Matrix**

You'll need:
- A Matrix account (https://app.element.io or self-hosted)
- A private room ID
- Invite the bot user to the room

This is complex and entirely optional. Leave it disabled for now.

### 4. Add YouTube channels

Go to **Channels** → **Add Channel**

1. Paste a channel URL (e.g., `https://www.youtube.com/@channelname`) or channel ID
2. Confirm the resolved channel metadata
3. Optionally set:
   - Max video age (days to look back)
   - Max videos per run
4. Save

Repeat for each channel you want to monitor.

### 5. Run ingestion

Go to **Ingestion Runs** → **Run Now**

This will:
1. Check your channels for new videos
2. Download transcripts (or generate from audio)
3. Extract screenshots, segments, and links
4. Scrape linked websites and repositories
5. Generate embeddings
6. Complete with a run summary

First run typically takes 5-15 minutes depending on video count and AI model speed.

### 6. Search your knowledge

Go to **Search**

1. Type a natural-language query or keywords
2. Adjust filters if needed (channel, date, type)
3. Review ranked results with explanation of why each matched
4. Click a result to jump to the YouTube timestamp or linked resource

## Ongoing operations

### Schedule automatic ingestion

Go to **Settings** → **Ingestion Schedule**

- Enable scheduled ingestion
- Set the time (e.g., 06:00 daily)
- Worker service will automatically run at that time

### Manually re-ingest a channel

Go to **Channels**, select a channel, click **Backfill**

Set:
- Days to look back
- Max videos

This re-checks channels for videos older than the normal lookback window.

### Retry failed ingestion

Go to **Ingestion Runs**, select a run, click items and retry.

### Monitor performance

Go to **Settings** → **Observability**

- **Grafana** (http://localhost:3000, admin/admin by default) — resource usage, ingestion metrics
- **Prometheus** (http://localhost:9090) — raw metrics
- **Loki** (http://localhost:3100) — centralized logs
- **Tempo** (http://localhost:3200) — distributed traces

These are read-only dashboards. Use them to understand what's happening but not to control behavior.

### Edit and correct metadata

Go to **Search** → open a result → **Edit**

You can override:
- Video title or description
- Segment title or summary
- Transcript text
- Link classification
- Repository metadata

Streaming Digest records history and regenerates embeddings with your corrections.

### Add private notes

Go to **Search** → open a result → **Notes**

Write markdown using the EasyMDE editor. Notes are:
- Private to you
- Searchable
- Attached to videos, segments, or links
- Editable anytime

## Backup and restore

### Backup

Back up these directories and files:

```bash
# PostgreSQL data
docker compose exec -T streaming-digest-postgres \
  pg_dump -U streamingdigest streamingdigest > backup.sql

# Screenshots volume
docker cp <container-id>:/screenshots ./screenshots-backup

# Configuration
cp .env ./env-backup
docker compose config > compose-backup.yaml
```

**Retention:** Keep the most recent weekly backups and daily backups for 30 days.

### Restore

```bash
# Stop the stack
docker compose down

# Restore PostgreSQL
docker compose up -d streaming-digest-postgres
docker compose exec -T streaming-digest-postgres \
  psql -U streamingdigest streamingdigest < backup.sql

# Restore screenshots
docker cp ./screenshots-backup <container-id>:/screenshots

# Restart
docker compose up -d
```

**Verify after restore:**
- Login works
- Search returns results
- Screenshots load
- Ingestion can run

## Troubleshooting

### Port already in use

If port 8080 or 5432 is already in use:

Edit `.env` and change:
```bash
# Change API port
STREAMING_DIGEST_API_PORT=8081

# Change PostgreSQL port
DATABASE_PORT=5433
```

Then restart:
```bash
docker compose down
docker compose up -d
```

### Out of disk space

Check available space:
```bash
df -h
```

Delete old screenshots:
```bash
docker compose exec streaming-digest-api \
  /bin/sh -c "rm -rf /screenshots/*"
```

Or reduce log retention:
```bash
# In .env, change:
LOKI_RETENTION_DAYS=7  # default is 30
```

### Database migration failed

If you see "migration" errors in logs:

```bash
# Restart just the API to re-run migrations
docker compose restart streaming-digest-api

# Check logs
docker compose logs streaming-digest-api
```

### Ollama out of memory

Ollama models need RAM to run. If you see OOM errors:

1. Stop the stack: `docker compose down`
2. Edit `.env`: increase Docker memory limit or reduce model size
3. Restart: `docker compose up -d`

Or disable Ollama entirely and use OpenAI API instead (advanced).

### Whisper taking too long

Local audio-to-text (Whisper) can take 30+ seconds per video. To speed up:

- Use GPU acceleration (requires NVIDIA CUDA or AMD HIP support)
- Skip auto-transcription: disable Whisper and use YouTube captions only

## Security notes

**Streaming Digest is designed for private, trusted networks only.** Do NOT expose it to the internet without:

1. **Authentication** — single-user login is the only auth mechanism; use Tailscale, VPN, or private network
2. **HTTPS** — all traffic should be encrypted; use a reverse proxy with TLS
3. **Rate limiting** — no built-in DDoS protection; use reverse proxy or network-level limits
4. **Backups** — protect backups with encryption and access control
5. **Secrets** — `.env` contains PostgreSQL credentials; don't commit to version control

Example: Deploy behind Tailscale proxy or nginx reverse proxy with authentication.

## Getting help

- **Documentation**: https://github.com/matthewcorven/streaming-digest/tree/main/docs
- **Issues**: https://github.com/matthewcorven/streaming-digest/issues
- **Discussions**: https://github.com/matthewcorven/streaming-digest/discussions

---

**Last updated:** 2026-08-11  
**Next step:** [Developer Onboarding Guide](./ONBOARDING_DEVELOPERS.md) if you want to contribute or customize
