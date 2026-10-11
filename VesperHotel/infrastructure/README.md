# Running VESPER

Run the commands below from the `VesperHotel` project directory. The PowerShell scripts work on Windows and do not change machine settings or launch Docker Desktop. Every credential is generated locally on first use. The `.env` file and `.local/` directory are ignored by Git and excluded from container builds.

## Local development

Requirements: .NET 10 SDK, Node.js 22.12 or newer, pnpm 10.11.0, and PowerShell. The launch script also recognizes the Codex bundled pnpm location.

```powershell
pwsh -File scripts/start-local.ps1
```

This builds the API and frontend, installs the locked frontend dependencies, and starts the API and a small local static server in hidden processes. The local mode uses a persistent SQLite database and runs persistent background jobs inside the API process. It does not require Docker, MySQL, Kafka, or Elasticsearch.

- Website: <http://localhost:5173>
- API health: <http://localhost:5080/api/health>
- Administrator: `admin@vesper.local`; read `ADMIN_PASSWORD` from the generated `.env`.
- Database: `.local/vesper.local.db`
- Local notification delivery: `.local/notifications/`
- Process logs: `.local/logs/`

Use `-SkipBuild -SkipInstall` when the code and dependencies have already been built. Changes to C# require stopping and restarting the API; Vite reloads frontend changes automatically.

```powershell
pwsh -File scripts/stop-local.ps1
```

Stopping preserves credentials and database contents. The script only stops the processes it recorded, after checking their IDs, start times, and executable paths. It does not kill unrelated Node or .NET processes. If a port is already occupied, startup fails with a useful message.

## Docker development stack

Requirements: Docker Desktop with the Linux engine running and Docker Compose v2. Give Docker enough memory for MySQL, Kafka, and Elastic together; 6 GB is a practical starting point for this development setup.

Stop local mode first because both modes use API port 5080.

```powershell
pwsh -File scripts/start-docker.ps1
```

The repository root also contains `compose.yaml`, so the standard foreground command works after `scripts/initialize-env.ps1` has created `.env`:

```powershell
docker compose --env-file .env up --build
```

This is the recommended way to run the complete application locally. The home page obtains Las Vegas conditions through the API's Open-Meteo client. The room catalogue includes separate cover images and room-detail galleries. Administrators sign in at <http://localhost:8080/admin/login>.

The stack builds the application containers and waits for health checks. It includes:

| Service | Purpose | Local address |
| --- | --- | --- |
| `web` | nginx serves the built React app and proxies `/api` | <http://localhost:8080> |
| `api` | ASP.NET Core API | <http://localhost:5080/api/health> |
| `worker` | Outbox delivery and booking lifecycle jobs | Internal process |
| `mysql` | Persistent application database | `localhost:3307` |
| `kafka` | KRaft event broker | `localhost:9094` |
| `elasticsearch` | Application log search | <http://localhost:9200> |
| `kibana` | Log exploration | <http://localhost:5601> |

All published ports bind to `127.0.0.1`. MySQL, Kafka, Elasticsearch, and application delivery files use persistent named volumes. Application code, downloaded packages, SQLite data, and local credentials are not mounted from the host. The API can start independently of Kafka and Elasticsearch; MySQL health is required. The worker starts after the API and broker are ready.

```powershell
pwsh -File scripts/docker-status.ps1
pwsh -File scripts/docker-status.ps1 -Logs
pwsh -File scripts/stop-docker.ps1
```

The stop command preserves all database volumes. Do not delete `.env` while retaining those volumes: existing database and administrator credentials will still be the old ones. A new generated admin password does not automatically change an existing account.

To validate configuration without a running Docker engine:

```powershell
pwsh -File scripts/start-docker.ps1 -ValidateOnly
```

This runs `docker compose config --quiet`, which avoids printing interpolated secrets. Docker CLI configuration is written under `.local/docker-config`, leaving global Docker settings untouched. Do not use raw `docker compose config` in a shared terminal or paste its output: the expanded configuration includes passwords.

## Configuration

`scripts/initialize-env.ps1` generates cryptographically random MySQL passwords, the JWT signing key, the seed administrator password, and the Kafka cluster ID. Re-running it preserves existing values. `.env.example` contains placeholders only.

The backend receives ordinary ASP.NET configuration keys:

| Key | Local mode | Docker mode |
| --- | --- | --- |
| `Database__Provider` | `Sqlite` | `MySql` |
| `ConnectionStrings__HotelDatabase` | Absolute SQLite file path | MySQL connection string |
| `Jwt__Key` | Generated signing key | Same generated signing key |
| `Jwt__Issuer` / `Jwt__Audience` | `VesperHotel` / `VesperHotel.Web` | Same |
| `Seed__AdminEmail` / `Seed__AdminPassword` | Generated local settings | Same |
| `Messaging__Mode` | `Local` | `Kafka` |
| `Kafka__BootstrapServers` | Empty | `kafka:9092` |
| `Kafka__Topic` | Not used | `vesper.bookings` |
| `Elasticsearch__Url` | Empty | `http://elasticsearch:9200` |
| `Frontend__Url` | `http://localhost:5173` | `http://localhost:8080` |
| `Notifications__Directory` | `.local/notifications` absolute path | `/app/data/notifications` |
| `DataProtection__Directory` | `.local/keys` absolute path | `/app/data/keys` |
| `Workers__RunInApi` | `true` | `false` |

Local launch restores the parent terminal's environment after starting its child processes. Compose port overrides live in `.env`. Keep local API/Vite ports at 5080/5173 because the Vite development proxy uses that API port.

## Deployment boundary

This Compose file is a complete local development topology. It uses HTTP, a single Kafka broker, and unauthenticated Elasticsearch inside the development network. For a public deployment, terminate HTTPS, set the application environment to Production, configure secure cookies and trusted proxies, enable infrastructure authentication/TLS, use an appropriate secrets service, and arrange database backups. The built nginx image supports browser route refreshes and sends `/api` requests to the API using the same origin.

## Image references

The image tags were selected from the vendors' published images; no service uses `latest`:

- [.NET runtime and SDK documentation](https://github.com/dotnet/dotnet-docker): `mcr.microsoft.com/dotnet/sdk:10.0`, `mcr.microsoft.com/dotnet/aspnet:10.0`
- [MySQL official image](https://hub.docker.com/_/mysql): `mysql:8.4.11`
- [Apache Kafka 4.1 quick start](https://kafka.apache.org/41/getting-started/quickstart/): `apache/kafka:4.1.2`
- [Elastic Docker guide](https://www.elastic.co/guide/en/elasticsearch/reference/8.19/docker.html): Elasticsearch and Kibana `8.19.23`
- [Node official image](https://hub.docker.com/_/node): `node:22.23.3-alpine`
- [nginx official image](https://hub.docker.com/_/nginx): `nginx:1.30.5-alpine`

Image availability and Compose syntax validation are separate from running the containers. A successful `-ValidateOnly` result does not prove that the engine, image builds, MySQL migrations, or broker connectivity have been tested.
