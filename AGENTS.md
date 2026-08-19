# Repository Instructions for Coding Agents

This document gives coding agents accurate context about the **Smart Parking Navigator** repository so they can work safely and consistently without inventing architecture or commands that are not present.

---

## Project Overview

The Smart Parking Navigator is a Singapore HDB car-park finder that combines static car-park data with live availability from the data.gov.sg API. The goal is a mobile-first experience where users search a destination and receive ranked, real-time parking recommendations.

See [`IDEATION.md`](IDEATION.md) for the full product concept.

---

## Repository Structure

```
/
├── data/                          # Static datasets and API contract
│   ├── HDBCarparkInformation.csv  # Static HDB car park metadata (2,016 records)
│   ├── CarparkAvailability.json   # Published data.gov.sg OpenAPI contract
│   ├── carpark-availability-sample.json  # Representative live API response
│   └── carpark-availability.http  # HTTP client requests for the data.gov.sg API
├── docs/                          # Workshop step-by-step guides
├── src/
│   ├── CarparkAvailability.ApiApp/       # ASP.NET Core Minimal API backend
│   ├── CarparkAvailability.WebApp/       # Blazor Server frontend
│   ├── CarparkAvailability.AppHost/      # .NET Aspire orchestration host
│   └── CarparkAvailability.ServiceDefaults/  # Shared Aspire service defaults
├── CarparkAvailability.slnx       # Solution file
├── Directory.Packages.props       # Central package version management
└── global.json                    # .NET SDK version pin (10.0.100)
```

---

## Technology Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 10 |
| Orchestration | .NET Aspire 13 |
| Backend | ASP.NET Core Minimal API |
| Frontend | Blazor Server (Interactive Server render mode) |
| Package management | Central Package Management (`Directory.Packages.props`) |
| Observability | OpenTelemetry (OTLP exporter, ASP.NET Core and HTTP instrumentation) |
| Service discovery | `Microsoft.Extensions.ServiceDiscovery` |
| Resilience | `Microsoft.Extensions.Http.Resilience` |

---

## Project Responsibilities

### `CarparkAvailability.ApiApp`

- Server-side ASP.NET Core Minimal API.
- Calls the data.gov.sg Carpark Availability API using the `DataGovSg__ApiKey` secret. **This call must never come from the browser.**
- Joins live availability data with static HDB information from `data/HDBCarparkInformation.csv`.
- Exposes a JSON HTTP API consumed by WebApp via Aspire service discovery.
- Receives the `DataGovSg__ApiKey` environment variable from AppHost at runtime.

### `CarparkAvailability.WebApp`

- Blazor Server application.
- Talks to ApiApp through the Aspire-injected HTTP client; it does **not** call data.gov.sg directly.
- Loads Google Maps using the `GoogleMaps__ApiKey` environment variable. The Google Maps API key is used only in browser-side JavaScript; it is **not** used in server-side code.
- Uses Interactive Server render mode (`AddInteractiveServerComponents`).

### `CarparkAvailability.AppHost`

- .NET Aspire orchestration host; the single entry point for local development.
- Wires ApiApp and WebApp together with `WithReference` and `WaitFor`.
- Injects secrets from .NET user secrets (`UserSecretsId: smart-parking-navigator-workshop-apphost`):
  - `GoogleMaps:ApiKey` → `google-maps-api-key` parameter → WebApp `GoogleMaps__ApiKey`
  - `DataGovSg:ApiKey` → `data-gov-sg-api-key` parameter → ApiApp `DataGovSg__ApiKey`

### `CarparkAvailability.ServiceDefaults`

- Shared Aspire service-defaults library added to both ApiApp and WebApp via `AddServiceDefaults()`.
- Configures OpenTelemetry, health checks, and service discovery for all projects.

---

## Data

- `data/HDBCarparkInformation.csv` — static HDB metadata. Join to live data using `car_park_no` ↔ `carpark_number`.
- `data/CarparkAvailability.json` — published OpenAPI specification for `https://api.data.gov.sg/v1/transport/carpark-availability`. Treat this as the primary API contract.
- `data/carpark-availability-sample.json` — representative live response. Use for offline development and contract testing.
- `data/carpark-availability.http` — HTTP client file for manual API exploration. Requires `DATA_GOV_SG_API_KEY` in a `.env` file at repository root (never commit this file).

---

## Commands

All commands must be run from the repository root unless stated otherwise.

### Restore dependencies

```bash
dotnet restore CarparkAvailability.slnx
```

### Build the solution

```bash
dotnet build CarparkAvailability.slnx
```

### Run the application (local development)

```bash
dotnet run --project src/CarparkAvailability.AppHost
```

This starts the Aspire dashboard, ApiApp, and WebApp. The Aspire dashboard URL is printed to the terminal.

### Run tests

No tests exist yet. When tests are added they must follow the conventions in the relevant acceptance criteria and be runnable with:

```bash
dotnet test CarparkAvailability.slnx
```

---

## Secret Handling

- **Never commit API keys, connection strings, or credentials** to source control.
- Store secrets for local development using .NET user secrets on the AppHost project:
  ```bash
  dotnet user-secrets --project src/CarparkAvailability.AppHost set "GoogleMaps:ApiKey" "<key>"
  dotnet user-secrets --project src/CarparkAvailability.AppHost set "DataGovSg:ApiKey" "<key>"
  ```
- See [`docs/google-maps-api-key.md`](docs/google-maps-api-key.md) and [`docs/data-gov-sg-api-key.md`](docs/data-gov-sg-api-key.md) for key-acquisition steps.
- A `.env` file at repository root (for the `.http` client file) is covered by `.gitignore`. Do not commit it.

---

## Service Boundaries

| Concern | Where it must run |
|---|---|
| Calls to `api.data.gov.sg` | Server side (ApiApp) only |
| Google Maps JavaScript API | Browser side (WebApp) only |
| SVY21 → WGS84 coordinate conversion | Server side (ApiApp) |

**Do not** add client-side code that calls `api.data.gov.sg` directly.

---

## Guardrails

- Do not invent project structure, namespaces, endpoints, or commands that are not present in the repository.
- Do not implement features that are not covered by reviewed and accepted PRD and TRD documents.
- Do not modify `global.json`, `Directory.Packages.props`, or `CarparkAvailability.slnx` without an explicit requirement.
- Do not downgrade or pin packages to versions that differ from the `10.*` / `1.*` floating ranges already in `Directory.Packages.props` without a documented reason.
- Treat `data/CarparkAvailability.json` as the authoritative API contract. Do not hard-code response shapes that differ from it.
- Generated code (scaffolding, source generators) must be reviewed before committing.

---

## Testing

- No tests exist yet. Tests must be added when features are implemented.
- Tests must follow the conventions established in the accepted TRD.
- Unit tests and integration tests go in a `tests/` directory at repository root.
- Contract tests for the data.gov.sg API must use `data/carpark-availability-sample.json` as the offline fixture.
- Tests must be runnable with `dotnet test CarparkAvailability.slnx`.

---

## Documentation

- `docs/` contains workshop step-by-step guides. Do not modify these unless correcting inaccuracies.
- `IDEATION.md` is the starting product concept and must not be modified.
- `PRD.md` and `TRD.md` are created during the workshop and must be reviewed before implementation work begins.
- Public API surfaces and significant architectural decisions must be documented in `TRD.md`.

---

## Git Commits and Pull Requests

- Write commit messages in the imperative mood: `Add SVY21 conversion helper`, not `Added` or `Adds`.
- Keep each commit focused on a single logical change.
- Reference the relevant issue number in the commit message or PR description: `Closes #<n>`.
- Pull requests must:
  - Pass `dotnet build CarparkAvailability.slnx` with no errors or warnings.
  - Pass `dotnet test CarparkAvailability.slnx` (once tests exist) with no failures.
  - Include a description that explains what changed and why.
  - Not include committed secrets, generated build artifacts, or `bin`/`obj` directories.
- Branch naming convention: `<type>/<short-description>`, for example `feat/carpark-list-endpoint` or `fix/coordinate-conversion`.
