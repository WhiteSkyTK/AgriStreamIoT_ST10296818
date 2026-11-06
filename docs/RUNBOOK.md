# AgriStream IoT runbook: deploy, load test, monitor, autoscale

Use one resource group and one region for everything (for example South Africa North).

## 1. Resources

| Resource | Settings |
|---|---|
| Storage account | StorageV2, Standard. The app creates the tables, containers and queue itself. |
| Azure SQL Database | Already created. In the SQL server's **Networking**: tick **Allow Azure services and resources to access this server** and add your client IP. |
| Application Insights | Workspace-based. Link it to both apps (below); that sets `APPLICATIONINSIGHTS_CONNECTION_STRING` for you. |
| Function App | Windows, **Consumption** plan, runtime **.NET 8 (Isolated)**, storage = the storage account above, Application Insights = on. Avoid the Flex Consumption plan: its blob trigger only works through Event Grid. |
| App Service plan | Windows, **Standard S1** or **Premium v3 P0v3**. Autoscale needs Standard or higher; Free and Basic cannot scale out by rule. |
| Web App | Runtime **.NET 9**, on the plan above, Application Insights = on. |

.NET 8 and .NET 9 reach end of support on 10 November 2026. If the portal stops offering them, publish with **Deployment mode: Self-contained** in the publish profile and the app keeps working.

## 2. App settings in Azure

**Function App > Settings > Environment variables > App settings**

| Name | Value |
|---|---|
| `FarmStorage` | storage account connection string |
| `FarmSql` | Azure SQL ADO.NET connection string with the password |

(`AzureWebJobsStorage`, `FUNCTIONS_WORKER_RUNTIME` and `APPLICATIONINSIGHTS_CONNECTION_STRING` are already there.)

**Web App > Settings > Environment variables**

| Tab | Name | Value |
|---|---|---|
| Connection strings | `FarmSql` (type SQLAzure) | Azure SQL connection string |
| Connection strings | `FarmStorage` (type Custom) | storage connection string |
| App settings | `AzureFunctions__BaseUrl` | `https://<function-app>.azurewebsites.net/api/` |
| App settings | `AzureFunctions__Key` | Function App > **App keys** > `default` host key |

Also on the Web App: **Configuration > General settings**: turn **ARR affinity Off** (so load spreads across instances) and **Always On On**.

## 3. Publish

1. Right-click `AgriStreamIoT.Functions` > **Publish** > Azure > Function App (Windows) > pick your Function App > Publish.
2. Right-click `AgriStreamIoT.Web` > **Publish** > Azure > App Service (Windows) > pick your Web App > Publish.
3. Open the web app, sign in as `admin@agristream.co.za`, open **Setup**: every component should be green.

## 4. Check the pipeline (screenshots)

- **Drone & media > Upload**: upload a few JPG/PNG files to *Drone images*. The audit list turns *Processed* within seconds (function 1, `ImageProcessingAudit` table).
- **Fields > North Pivot 1**: the dashboard data comes from function 2; the line under the gauge names the GetLatestTelemetry function instead of *Direct Table Storage query*.
- **Trigger irrigation** on a field: function 3 queues the command, function 4 sets the controller to *Irrigating* in Azure SQL. The tracker on the page follows it; the **Device register** shows the new status and history.
- **Pipeline** (admin): health of every component, queue depth, poison queue, the simulator switch and the *Sensor burst* and *Drone flight burst* forms for peak-hour demos.
- Portal: Function App > Functions > each function > **Invocations** (or Monitor) for the execution log.

## 5. Load test

The `/api/analytics/outlook` endpoint runs a 20,000-path Monte Carlo moisture forecast per request, so it drives App Service CPU up quickly. It needs no login.

**Option A: the included console tester** (from the solution folder):

```
dotnet run -c Release --project AgriStreamIoT.LoadTester -- --url https://<web-app>.azurewebsites.net --mode cpu --users 60 --duration 900 --ramp 120
```

It prints requests per second, p50/p95 latency, errors and how many instances answered (from the `X-Served-By` header), and saves `loadtest-results.csv`.

**Option B: Azure Load Testing**: create an *Azure Load Testing* resource > **Create > Create a URL-based test** > URL `https://<web-app>.azurewebsites.net/api/analytics/outlook?fieldId=F01` (add a second request for `/api/farm/summary`) > Load: 50 virtual users, 15 min duration, 2 min ramp-up > Run. Screenshot the dashboard (virtual users, response time, requests/s, errors). Under **App Components** add the App Service plan to see its CPU next to the test results.

## 6. Autoscale

App Service plan > **Scale out (App Service plan)** > **Rules Based** > **Manage rules based scaling** > **Custom autoscale**:

- Instance limits: minimum 1, maximum 3, default 1.
- Scale out rule 1: *CPU Percentage* > 70, average over 5 minutes > increase count by 1, cool down 5 minutes.
- Scale out rule 2: *Memory Percentage* > 75, average over 5 minutes > increase count by 1, cool down 5 minutes.
- Scale in rule 1: *CPU Percentage* < 30, average over 10 minutes > decrease count by 1, cool down 10 minutes.
- Scale in rule 2: *Memory Percentage* < 50, average over 10 minutes > decrease count by 1, cool down 10 minutes.
- Save. Run the load test again: **Run history** shows the instance count going 1 > 2 > 3 and back down afterwards. The load tester's *instances* column and the **Pipeline** page ("served by instance …") confirm it.

The Function App on the Consumption plan scales by itself on queue length and incoming events; no rule is needed.

## 7. Azure Monitor

**Metrics** (App Service plan): *CPU Percentage* and *Memory Percentage*, split by *Instance*, last 1 hour. **Metrics** (Web App): *Requests*, *Response Time*, *Http 5xx*. **Metrics** (Function App): *Function Execution Count*. **Metrics** (Storage account, Queue): *Queue Message Count*. Pin them to a dashboard for one screenshot.

**Application Insights > Live metrics** during the load test shows each server instance joining.

**Application Insights > Logs** (KQL):

```kusto
// Requests per instance during the test (shows scale-out)
requests
| where timestamp > ago(1h)
| summarize requests = count() by cloud_RoleInstance, bin(timestamp, 1m)
| render timechart

// Latency percentiles per minute
requests
| where timestamp > ago(1h) and cloud_RoleName !contains "func"
| summarize p50 = percentile(duration, 50), p95 = percentile(duration, 95), failed = countif(success == false) by bin(timestamp, 1m)
| render timechart

// Function executions and their log lines
traces
| where timestamp > ago(1h) and message has_any ("Drone image", "irrigation command", "Irrigation command")
| project timestamp, cloud_RoleInstance, message
| order by timestamp desc
```

**Alerts** (optional): Web App > Alerts > Create > *Response Time* > 2 seconds average over 5 minutes, and *Http 5xx* > 5 in 5 minutes.

## 8. Screenshot checklist

1. Resource group showing all resources.
2. Web app: farm overview, a field dashboard, device register, drone upload with audit list, telemetry explorer, users page.
3. Storage: the four tables with rows (Storage browser), the two containers, the queue.
4. Azure SQL: Query editor `SELECT * FROM dbo.Devices` and `dbo.DeviceStatusHistory` after an irrigation command.
5. Function App: the four functions and an invocation log for each.
6. Load test results (Azure Load Testing dashboard or the console output and CSV).
7. Autoscale settings (the four rules) and **Run history** showing scale-out and scale-in.
8. Azure Monitor metrics (CPU and memory by instance, requests, response time) and the KQL results.
