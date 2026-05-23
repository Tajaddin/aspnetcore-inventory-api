# aspnetcore-inventory-api

> ASP.NET Core 8 minimal API for an inventory catalog. **1,482 req/s at p99 58ms** on a paginated list endpoint (single instance, SQLite demo provider, 50 concurrent clients, 5000/5000 OK). EF Core + Postgres + migrations, atomic stock reservation, RFC 7807 problem details, 16 tests on SQLite with zero Docker, 87% coverage.

[![ci](https://github.com/Tajaddin/aspnetcore-inventory-api/actions/workflows/ci.yml/badge.svg)](https://github.com/Tajaddin/aspnetcore-inventory-api/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](src/InventoryApi/InventoryApi.csproj)

## Hero metrics

Reproducible with zero setup (no Docker, no Postgres):

```bash
DB_PROVIDER=sqlite ASPNETCORE_URLS=http://localhost:5080 dotnet run --project src/InventoryApi
python load/load_probe.py --base http://localhost:5080 --requests 5000 --concurrency 50
```

Last measured baseline (3 consecutive runs, full output and hardware in [`bench/results.txt`](bench/results.txt)):

| Metric | Value |
|---|---:|
| **Throughput** | **1,482 req/s** (run-to-run median: 1,526 rps on i7-10875H) |
| Latency p99 | **~58 ms** |
| Success rate | 5000 / 5000 (100%) |

Measured on `GET /api/products?size=20` (paginated, category-filterable) against a single instance on the SQLite demo provider, driven by the standard-library Python probe at concurrency 50. Minimal APIs + EF Core keep per-request overhead low.

## What it is

A product/inventory REST API built with the modern .NET stack:

| Concern | Implementation |
|---|---|
| Endpoints | ASP.NET Core 8 **minimal APIs** with `MapGroup`, typed `Results` |
| Persistence | EF Core 8, **PostgreSQL** (Npgsql) in production with Flyway-style EF **migrations**; SQLite for tests + demo |
| Domain | Products with SKU uniqueness, category enum, price precision, stock |
| Atomic op | `POST /reserve` decrements stock transactionally, rejects over-reservation with 409 |
| Validation | DataAnnotations on request records (property-targeted), per-field 400s |
| Errors | RFC 7807 **ProblemDetails** for 404 / 409 / validation, via `AddProblemDetails` + `UseExceptionHandler` |
| Health | `/healthz` health check endpoint |
| Tests | 16: service unit tests + full-pipeline integration tests via `WebApplicationFactory`, all on in-memory SQLite |

## Why this matters for hiring

Role categories unlocked: **Backend-.NET / C#**, Microsoft-stack, enterprise software.

Modern .NET shops want minimal APIs, EF Core, and ProblemDetails, not legacy controllers. This repo backs the ".NET / C#" resume line with a tested, load-measured service.

## Run it

### Zero-setup demo (SQLite)

```bash
DB_PROVIDER=sqlite dotnet run --project src/InventoryApi
# API on http://localhost:5000 (or ASPNETCORE_URLS to override)
```

```bash
curl -s -XPOST localhost:5080/api/products -H 'content-type: application/json' \
  -d '{"name":"Mechanical Keyboard","sku":"KB-1","price":89.99,"stockQuantity":50,"category":"Electronics"}'

curl -s 'localhost:5080/api/products?category=Electronics&size=20'
curl -s -XPOST localhost:5080/api/products/1/reserve -H 'content-type: application/json' -d '{"quantity":3}'
```

### Production stack (Postgres via Docker)

```bash
docker compose up --build   # app on :8080, Postgres on :5432, EF migrations on boot
```

## API

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/products` | List (params: `category`, `page`, `size`) |
| GET | `/api/products/{id}` | Get one |
| POST | `/api/products` | Create (409 on duplicate SKU) |
| PUT | `/api/products/{id}` | Partial update (null fields untouched) |
| DELETE | `/api/products/{id}` | Delete |
| POST | `/api/products/{id}/reserve` | Atomically reserve stock (409 if insufficient) |
| GET | `/healthz` | Health check |

All error responses are `application/problem+json` (RFC 7807).

## Testing

```bash
dotnet test                                                  # 16 tests on SQLite, no Docker
dotnet test --collect:"XPlat Code Coverage" --settings coverlet.runsettings
```

- **ProductServiceTests** (9): create/get/list-filter/update-partial/delete/reserve (available, insufficient, missing)/SKU-exists. Fresh in-memory SQLite per test.
- **ApiIntegrationTests** (7): full minimal-API pipeline via `WebApplicationFactory` — health, CRUD roundtrip, 404 problem shape, validation 400, duplicate-SKU 409, reserve-over-stock 409, delete 204-then-404.

Coverage 87% (line, excluding generated EF migrations).

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
load/load_probe.py           # stdlib HTTP load probe
```

## Stack

C# 12 / .NET 8, ASP.NET Core minimal APIs, EF Core 8, Npgsql (Postgres), SQLite (test + demo), xUnit, FluentAssertions, coverlet, Docker (alpine multi-stage), GitHub Actions.

## License

MIT
