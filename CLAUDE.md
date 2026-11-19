# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

.NET 10 console app, shipped as a Docker image for Docker Hub, that fetches the USD/MXN FIX exchange rate (the one published in the DOF) from the Banxico SIE API and upserts it into **SQL Server**. `README.md` (Spanish) is the source of truth: §1–10 are the functional spec, §11 documents the app (commands, env vars, exit codes, Docker Hub publishing, scheduling). Code identifiers, logs and docs are in Spanish; keep it that way.

## Commands

```bash
dotnet build
dotnet test
dotnet test --filter "FullyQualifiedName~ValidadorTests"     # single test class
dotnet run --project src/TipoCambioDof -- ayuda               # diario | historico [--desde/--hasta yyyy-MM-dd] | crear-tabla
docker run --rm -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet test   # if the local SDK is older than 10
docker build -t tipo-cambio-dof:local .
docker compose run --rm app crear-tabla      # runs the image against the DB in .env's SQL_CONNECTION_STRING (no bundled SQL Server)
```

`TreatWarningsAsErrors` is on in the app project.

## Architecture

- **Run-once container**: `Program.cs` parses the command (`Comando.cs`), builds a generic host only for DI/config/logging (the host is never `Run()`), executes one process, and returns an exit code: 0 ok, 1 error, 2 finished with alerts (`Alertador.Enviadas > 0`), 64 bad args. Scheduling (Mon–Fri 13:00 CDMX) is external (cron / k8s CronJob); the 30-min retry loop until 16:00 lives inside `ProcesoDiario`. SIGTERM/SIGINT cancel the shared token.
- **Pipeline**: `ProcesoDiario` / `ProcesoHistorico` → `Sincronizador` (fetch → `Validador` → `RepositorioTipoCambio.UpsertAsync`) → alerts. Both processes reuse `Sincronizador` for any date range.
- **Config** comes only from flat env var names (`BANXICO_TOKEN`, `SQL_CONNECTION_STRING`, `ALERT_WEBHOOK_URL`, `DIAS_ATRAS`, …) parsed in `Configuracion.Desde`; there is no appsettings.json. Defaults mirror README §5. For local debugging (Docker is only for production), `ArchivoEnv` loads the repo-root `.env` into the process environment without overriding real env vars; it is skipped when `DOTNET_RUNNING_IN_CONTAINER=true`.
- **Time**: all "today"/deadline logic goes through `Reloj` (America/Mexico_City, falls back to fixed UTC−6) backed by `TimeProvider`, so it is testable. Don't use `DateTime.Now`.
- **DB**: `Datos/Esquema.sql` is an embedded resource (logical name `Esquema.sql`) run by `crear-tabla`; it must stay a single idempotent batch (no `GO`). Upsert is a per-row `MERGE ... OUTPUT $action, deleted.valor` inside one transaction; value changes are written to `dbo.tipo_cambio_auditoria`. Timestamps are `DATETIME2(0)`: `tipo_cambio.fecha_captura`/`fecha_actualizacion` use `GETDATE()` (SQL server local time, SPEC 02, user requirement); `tipo_cambio_auditoria.fecha_cambio` stays UTC.
- **HTTP**: typed `BanxicoClient` with `AllowAutoRedirect = false` (so a 302 for an unknown series becomes an error); retries 1s/5s/30s only on network errors, timeouts and 5xx.

## Domain rules that are easy to get wrong

- **Use series `SF60653`** (settlement-date FIX). It has a value for every calendar day, including weekends and holidays (it repeats the last published value). `SF43718` is the determination-date series and only has bank business days.
- **Date offset:** the value the DOF publishes on day *D* appears in SF60653 dated *D + 1* (calendar day, not business day). So:
  - Applicable rate for date X = `SF60653[X]` (main use case; works for any day).
  - DOF-published value for date D = `SF60653[D + 1]`, valid only if D was a publication day. Detecting publication days requires `SF43718[D]` existing or a Banxico business-day calendar.
  - DB column `fecha_publicacion_dof = fecha_aplicacion − 1 day`.
- SF60653 contains **future dates** once the FIX is determined (after 12:00 Mexico City time), up to the next business day.

## Banxico SIE API quirks

- Base URL `https://www.banxico.org.mx/SieAPIRest/service/v1`; prefer the range endpoint `/series/SF60653/datos/{yyyy-MM-dd}/{yyyy-MM-dd}`.
- Token goes in the `Bmx-Token` header (not `?token=`, to keep it out of logs), read from an env var such as `BANXICO_TOKEN`. API requires TLS 1.3.
- Request dates are `yyyy-MM-dd`, but response `fecha` is **`dd/MM/yyyy`**, and `dato` is a **string** that may be non-numeric (skip and log those).
- An empty range returns HTTP 200 with the `datos` property **absent**, not an empty array.
- Invalid token / bad date format → HTTP 400 with `error.mensaje`; a nonexistent series → HTTP 302, which must be treated as an error (don't follow it silently).
- Rate limits: range endpoint 200 req / 5 min and 10,000 / day; `oportuno` endpoint 80 / min. Historical backfill (data since 1991-11-14) should go in one-year chunks with 1–2 s pauses.

## Daily job design (README §5)

Scheduled Mon–Fri 13:00 America/Mexico_City → fetch range `[today − 10, today + DIAS_ADELANTE]` (default 1: **ranges end at today's DOF publication**, i.e. SF60653 date today + 1 by the D + 1 rule; later values are intentionally not stored, user requirement) → validate (value within 10–40, alert but still save if >5% day-over-day change) → idempotent upsert keyed on `(fecha_aplicacion, moneda)`, updating `fecha_actualizacion` and audit-logging only when the value changed → verify the coverage row exists (`ProcesoDiario.FechaCobertura`: with the default, the row whose `fecha_publicacion_dof` is today); retry every 30 min until 16:00, then alert. `historico` clamps `--hasta` to today + DIAS_ADELANTE. The wide fetch range is deliberate (self-healing after failed runs, captures weekend values and Banxico revisions); don't narrow it to a single day.
