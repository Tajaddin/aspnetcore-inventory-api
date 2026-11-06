# aspnetcore-inventory-api

> ASP.NET Core 8 minimal API for an inventory catalog. **1,526 req/s at p99 57 ms** on a paginated list endpoint (median of 3 runs; single instance, SQLite demo provider, 50 concurrent clients, 5000/5000 OK). EF Core + Postgres + migrations, atomic stock reservation, RFC 7807 problem details, 21 tests on SQLite with zero Docker, 91% line coverage.

[![ci](https://github.com/Tajaddin/aspnetcore-inventory-api/actions/workflows/ci.yml/badge.svg)](https://github.com/Tajaddin/aspnetcore-inventory-api/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](src/InventoryApi/InventoryApi.csproj)

## Hero metrics

Reproducible with zero setup (no Docker, no Postgres):

```bash
DB_PROVIDER=sqlite ASPNETCORE_URLS=http://localhost:5080 dotnet run --no-launch-profile --project src/InventoryApi
python load/load_probe.py --base http://localhost:5080 --requests 5000 --concurrency 50
```

Last measured baseline (3 consecutive runs, full output and hardware in [`bench/results.txt`](bench/results.txt)):

| Metric | Value |
|---|---:|
| **Throughput** | **1,526 req/s** median of 3 runs (range 1,436-1,593) |
| Latency p99 | **57 ms** median run (range 56.6-60.8 ms) |
| Success rate | 5000 / 5000 (100%) |

Measured on `GET /api/products?size=20` (paginated, category-filterable) against a single instance on the SQLite demo provider, driven by the standard-library Python probe at concurrency 50. Minimal APIs + EF Core keep per-request overhead low.

## What it is

A product/inventory REST API built with the modern .NET stack:

| Concern | Implementation |
|---|---|
| Endpoints | ASP.NET Core 8 **minimal APIs** with `MapGroup`, typed `Results` |
| Persistence | EF Core 8, **PostgreSQL** (Npgsql) in production with Flyway-style EF **migrations**; SQLite for tests + demo |
| Domain | Products with SKU uniqueness, category enum, price precision, stock |
| Atomic op | `POST /reserve` is one conditional `UPDATE ... WHERE StockQuantity >= quantity`, so concurrent reservations cannot oversell; 409 when stock is short, 404 when the product does not exist. A `CHECK (StockQuantity >= 0)` constraint backs it in the database |
| Validation | DataAnnotations on request records (property-targeted), per-field 400s |
| Errors | RFC 7807 **ProblemDetails** for 404 / 409 / validation, via `AddProblemDetails` + `UseExceptionHandler` |
| Health | `/healthz` health check endpoint |
| Tests | 21: service unit tests + full-pipeline integration tests via `WebApplicationFactory` on in-memory SQLite, plus a duplicate-SKU create race over HTTP and a reservation race on a shared SQLite file that the `postgres` CI job repeats on PostgreSQL 16 |

## Why this matters for hiring

Role categories unlocked: **Backend-.NET / C#**, Microsoft-stack, enterprise software.

Modern .NET shops want minimal APIs, EF Core, and ProblemDetails, not legacy controllers. This repo backs the ".NET / C#" resume line with a tested, load-measured service.

## How to run

Prerequisites: .NET 8 SDK (Docker optional for the Postgres stack).

```bash
dotnet test                                                    # 21 tests on SQLite
DB_PROVIDER=sqlite ASPNETCORE_URLS=http://localhost:5080 \
  dotnet run --no-launch-profile --project src/InventoryApi    # zero-setup demo
python load/load_probe.py --base http://localhost:5080 \
       --requests 5000 --concurrency 50                        # reproduces the hero
```

### Zero-setup demo (SQLite)

```bash
DB_PROVIDER=sqlite ASPNETCORE_URLS=http://localhost:5080 dotnet run --no-launch-profile --project src/InventoryApi
# API on http://localhost:5080; without --no-launch-profile, launchSettings.json moves it to :5086
```

```bash
curl -s -XPOST localhost:5080/api/products -H 'content-type: application/json' \
  -d '{"name":"Mechanical Keyboard","sku":"KB-1","price":89.99,"stockQuantity":50,"category":1}'

curl -s 'localhost:5080/api/products?category=Electronics&size=20'
curl -s -XPOST localhost:5080/api/products/1/reserve -H 'content-type: application/json' -d '{"quantity":3}'
```

JSON bodies carry `category` as a number (0 Other, 1 Electronics, 2 Apparel, 3 Home, 4 Food); the `category` query parameter also takes the name.

### Production stack (Postgres via Docker)

```bash
docker compose up --build   # app on :8080, Postgres on :5432, EF migrations on boot
```

## API

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/products` | List (params: `category`, `page`, `size`) |
| GET | `/api/products/{id}` | Get one |
| POST | `/api/products` | Create (409 on duplicate SKU, including two concurrent creates) |
| PUT | `/api/products/{id}` | Partial update (null fields untouched) |
| DELETE | `/api/products/{id}` | Delete |
| POST | `/api/products/{id}/reserve` | Atomically reserve stock (409 if insufficient, 404 if missing) |
| GET | `/healthz` | Health check |

All error responses are `application/problem+json` (RFC 7807).

## Testing

```bash
dotnet test                                                  # 21 tests on SQLite, no Docker
dotnet test --collect:"XPlat Code Coverage" --settings coverlet.runsettings

# the reservation race on PostgreSQL, on a throwaway database built by the EF migrations
TEST_POSTGRES_CONNECTION="Host=localhost;Username=postgres;Password=..." \
  dotnet test --filter FullyQualifiedName~ReserveConcurrencyTests
```

- **ProductServiceTests** (11): create/get/list-filter/update-partial/delete/reserve (available, insufficient, missing)/negative stock rejected by the CHECK constraint/unique-index violation mapped to `DuplicateSkuException`/SKU-exists. Fresh in-memory SQLite per test.
- **ApiIntegrationTests** (8): full minimal-API pipeline via `WebApplicationFactory` — health, CRUD roundtrip, 404 problem shape, validation 400, duplicate-SKU 409, reserve-over-stock 409, reserve-missing 404, delete 204-then-404.
- **ReserveConcurrencyTests** (1): 20 concurrent reservations of 1 unit against stock 10, each with its own connection to a shared SQLite file. Exactly 10 succeed, each reports the stock its own update left (9 down to 0), and stock ends at 0. The earlier read-check-save version let all 20 through. With `TEST_POSTGRES_CONNECTION` set it runs on PostgreSQL instead; the `postgres` CI job does this against a `postgres:16-alpine` service.
- **ConcurrentCreateTests** (1): 20 concurrent creates of one SKU through the full pipeline, on a temp-file SQLite database with a connection per request. Exactly one 201 and nineteen 409s; creates that pass the SKU pre-check and then hit the unique index get 409, not 500.

Line coverage 91.55% (206 of 225 lines, excluding generated EF migrations), from `dotnet test -c Release --collect:"XPlat Code Coverage" --settings coverlet.runsettings`.

## Project layout

```
src/InventoryApi/
  Program.cs                 # minimal-API host, provider switch (postgres|sqlite), ProblemDetails
  Domain/                    # Product, Category
  Data/AppDbContext.cs       # EF model config (SKU unique, price precision, enum-as-string)
  Dtos/ProductDtos.cs        # request/response records, property-targeted validation
  Services/ProductService.cs # business logic incl. atomic reserve
  Endpoints/ProductEndpoints.cs
  Common/Validation.cs       # DataAnnotations -> ValidationProblem helper
  Migrations/                # EF migrations (Postgres)
tests/InventoryApi.Tests/
  CustomWebAppFactory.cs     # WebApplicationFactory with SQLite swap
  ProductServiceTests.cs
  ApiIntegrationTests.cs
  ConcurrentCreateTests.cs   # duplicate-SKU race over HTTP (temp-file SQLite host)
  ReserveConcurrencyTests.cs # reservation race (SQLite file, or Postgres via TEST_POSTGRES_CONNECTION)
load/load_probe.py           # stdlib HTTP load probe
```

## Stack

C# 12 / .NET 8, ASP.NET Core minimal APIs, EF Core 8, Npgsql (Postgres), SQLite (test + demo), xUnit, FluentAssertions, coverlet, Docker (alpine multi-stage), GitHub Actions.

## License

MIT
