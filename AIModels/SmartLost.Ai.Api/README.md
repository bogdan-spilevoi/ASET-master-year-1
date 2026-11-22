# Laptop inference API

One ASP.NET Core process listens on `http://127.0.0.1:5050`. It accepts one inference request at a time across both routes. Each request starts the corresponding Release CLI, reads its JSON output, waits for process exit and deletes the temporary image. Cancellation, server shutdown and a three-minute deadline terminate an active child before the slot is released. Run one API instance on the laptop.

| POST route | Raw request body | Successful response |
| --- | --- | --- |
| `/embeddings` | JPEG or PNG | DINO model ID, preprocessing version, dimensions and normalized vector |
| `/photo-analysis` | JPEG or PNG | Qwen JSON containing `objects` and `uncertainties` |

Both routes require the `X-Api-Key` header and the matching `image/jpeg` or `image/png` content type. Uploads are limited to 10 MiB and 4 megapixels. A missing or incorrect key returns 401. A busy laptop returns 429 with `Retry-After: 10`. An unsuccessful model process returns 502.

## Start on Windows

From the repository root in PowerShell, use the CPU configuration and model downloads already tested with the [DINO](../DINOv3-ViTL16-ONNX/README.md) and [Qwen](../Qwen3-VL-4B-Instruct-ONNX/README.md) consoles. Save one random shared API key in your password manager. Enter it at the prompt on the laptop and supply the same value to Matching.

```powershell
cd AIModels
dotnet restore AIModels.slnx --locked-mode
dotnet build AIModels.slnx -c Release --no-restore
cd SmartLost.Ai.Api
$secret = Read-Host "Shared AI API key" -AsSecureString
$env:AiRuntime__ApiKey = [System.Net.NetworkCredential]::new("", $secret).Password
dotnet run --no-build -c Release
```

The API locates each model DLL in its sibling project's Release output. Published copies can set `AiRuntime:DinoAssembly` and `AiRuntime:QwenAssembly` to absolute DLL paths. Each CLI reads its own settings beside its DLL.

In a second terminal, start the configured ngrok agent:

```powershell
ngrok http 5050
```

Keep both terminals open. Copy the HTTPS origin from ngrok into Matching. To test the API directly, send image bytes with the matching content type and key, for example in a PowerShell session where the same environment variable is set:

```powershell
Invoke-RestMethod -Method Post -Uri http://127.0.0.1:5050/embeddings -ContentType image/jpeg -InFile .\test.jpg -Headers @{ "X-Api-Key" = $env:AiRuntime__ApiKey }
```

## Connect Matching

Set the following section in Matching's local appsettings or its Kubernetes `matching-settings` ConfigMap. Replace the example origin with your tunnel's HTTPS address. Set the shared key separately via `AiRuntime__ApiKey`. The ngrok agent's account token stays on the laptop.

```json
"AiRuntime": {
  "BaseUrl": "https://your-tunnel.example",
  "CallTimeout": "00:02:00",
  "PhotoDirectory": "/app/ai-photos"
}
```

HTTP mode reads test photos by listing ID, photo ID and revision from `PhotoDirectory`. For a local .NET run, set this to an absolute directory on the Matching machine. Supply a file named `<listing-guid>_<photo-guid>_<revision>.jpg` or `.png`. Empty URL selects the existing explicitly enabled mock adapters. HTTP embeddings are stored under an `onnx:` model ID prefix to keep historical simulated vectors separate. Seed the scenario in a **disposable Matching demo database** if it needs tasks for this mode. The existing demo seed endpoint replaces all Matching rows.

For the shared Kind cluster, the Deployment reads the key from Secret `matching-ai-runtime` and mounts optional ConfigMap `matching-ai-test-photos` on every replica. On the cluster machine, prepare a file outside Git containing only the key (no trailing newline) and a small demo JPEG under 1 MiB to fit the ConfigMap. From the repository root, with namespace `smartlost` already created:

```sh
kubectl --context kind-smartlost-local -n smartlost create secret generic matching-ai-runtime --from-file=api-key=/path/to/key-file
kubectl --context kind-smartlost-local -n smartlost create configmap matching-ai-test-photos --from-file=10000000-0000-0000-0000-000000000001_10000000-f0f0-0000-0000-000000000001_1.jpg=/path/to/test.jpg
```

Set `AiRuntime.BaseUrl` in the JSON in [the Matching ConfigMap](../../deploy/kubernetes/base/matching/configmap.yaml) before running `./k8s.sh deploy matching`. Both pods then receive the same URL and key. If you change live settings or the Secret directly, restart the Deployment so the subPath-mounted settings and environment are refreshed. Larger images can use a read-only volume at the same directory. Use the [Matching requests](../../src/Services/Matching/SmartLost.MatchingService.Api/MatchingService.Api.http) for the seeded source listing. The HTTP mode reads mounted images until Listings implements photo download.
