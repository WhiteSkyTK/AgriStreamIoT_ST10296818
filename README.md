# AgriStream IoT

Smart monitoring for a large commercial farm. Soil sensors stream readings all day, drones upload bursts of imagery during daylight flights, and farmers trigger irrigation from a dashboard. The design keeps every write durable (Table Storage, Blob Storage, Storage Queue) so peak daylight load never loses data.

GitHub: _add your repository link here_

## What is in the solution

| Project | Role |
|---|---|
| `AgriStreamIoT.Web` | ASP.NET Core MVC app (App Service). Logins with roles, farm and field dashboards, master device register, drone/media upload, telemetry explorer, pipeline monitor. |
| `AgriStreamIoT.Functions` | Azure Functions (.NET 8 isolated), exactly four functions (below). |
| `AgriStreamIoT.Shared` | Storage names, Table entities, the irrigation command contract, telemetry queries and the Azure SQL device register. |
| `AgriStreamIoT.LoadTester` | Console load generator for the autoscale test. |

### The four functions

| # | Function | Trigger | What it does |
|---|---|---|---|
| 1 | `ProcessDroneImageUpload` | Blob (`drone-images/{name}`) | Logs the upload, validates the image header, writes a processing audit entry to the `ImageProcessingAudit` table. |
| 2 | `GetLatestTelemetry` | HTTP GET `/api/telemetry/latest` | Returns the latest sensor telemetry for the field dashboards (10 s cache). |
| 3 | `TriggerIrrigation` | HTTP POST `/api/irrigation/trigger` | Validates the "Trigger Irrigation" request and puts a command on the `irrigation-commands` Storage Queue (202 Accepted). |
| 4 | `ProcessIrrigationCommand` | Queue (`irrigation-commands`) | Updates the controller's operational status in Azure SQL (`dbo.Devices` + history), records the event in Table Storage, schedules the automatic stop. |

### Data stores

- **Azure SQL Database**: `dbo.Users` (Farmer, Agronomist, Admin), `dbo.Fields`, `dbo.Devices` (master device register with operational status), `dbo.DeviceStatusHistory`. The schema is created automatically on first start.
- **Table Storage**: `SensorTelemetry`, `IrrigationEvents`, `EnvironmentalLogs`, `ImageProcessingAudit`. Sample data is written on first start and the simulator keeps adding readings every 2 minutes.
- **Blob Storage**: `drone-images` (triggers function 1) and `farm-media`.
- **Queue Storage**: `irrigation-commands` (+ `irrigation-commands-poison` after 5 failed attempts).

## Run it locally (Visual Studio 2022)

1. Paste your connection strings (these files are git-ignored and stay on your PC):
   - `AgriStreamIoT.Web/appsettings.Local.json`: `ConnectionStrings:FarmSql` and `ConnectionStrings:FarmStorage`.
   - `AgriStreamIoT.Functions/local.settings.json`: `AzureWebJobsStorage`, `FarmStorage` (same storage string) and `FarmSql`.
   - Optional: `APPLICATIONINSIGHTS_CONNECTION_STRING` takes the **Application Insights** string (it starts with `InstrumentationKey=`), never the storage string.
2. In the Azure SQL server's **Networking** page, add your client IP address.
3. Right-click the solution, **Configure Startup Projects**, choose **Multiple startup projects** and start both `AgriStreamIoT.Functions` and `AgriStreamIoT.Web` (the included `.slnLaunch.user` does this).
4. Press F5. The web app opens on https://localhost:7091 and calls the functions on http://localhost:7071/api/.

The first start creates the SQL tables, the storage tables, containers and queue, and the demo data. The **Setup** page (`/Home/Setup`) shows what is connected.

### Demo logins

| Role | Email | Password |
|---|---|---|
| Admin | admin@agristream.co.za | Admin@2026 |
| Farmer | farmer@agristream.co.za | Farmer@2026 |
| Agronomist | agronomist@agristream.co.za | Agronomist@2026 |

Farmers and admins can trigger irrigation; agronomists and admins can change field thresholds; only admins manage devices, users and the pipeline simulator.

## Deploy, load test, monitor and autoscale

Step-by-step portal instructions and the screenshot checklist are in [`docs/RUNBOOK.md`](docs/RUNBOOK.md). Theory answers are in [`docs/Theory-Answers.txt`](docs/Theory-Answers.txt).

## Keeping secrets out of GitHub

`appsettings.Local.json` and `local.settings.json` are listed in `.gitignore`. In Azure the same values go in each app's **Settings > Environment variables**, never in a committed file.
