# Laptop inference API

One ASP.NET Core process listens on `http://127.0.0.1:5050`. Each model has its own inference slot, so DINO and Qwen can run concurrently. Each model accepts one active request at a time. Each request starts the corresponding Release CLI, reads its JSON output, waits for process exit and deletes the temporary image. Cancellation, server shutdown and a three-minute deadline terminate an active child before the slot is released. Run one API instance on the laptop.

| POST route | Raw request body | Successful response |
| --- | --- | --- |
| `/embeddings` | JPEG or PNG | DINO model ID, preprocessing version, dimensions and normalized vector |
| `/photo-analysis` | JPEG or PNG | Qwen JSON containing `objects` and `uncertainties` |

Both routes require the `X-Api-Key` header and the matching `image/jpeg` or `image/png` content type. Uploads are limited to 10 MiB and 4 megapixels. A missing or incorrect key returns 401. A request for a model that is already busy returns 429 with `Retry-After: 10`. An unsuccessful model process returns 502.

## Shared API key setup on macOS and Windows

Ask the person running the AI laptop for the team's shared API key and HTTPS tunnel URL. For a new installation, generate a random 32-character alphanumeric key once in a password manager and share it through the team's vault. Every client of that laptop uses the same value. The ngrok account token is a separate credential used by the tunnel agent.

| What runs on your device? | What to configure |
| --- | --- |
| Matching in your own Kind cluster | Set the tunnel URL and create the Secret once in your cluster. The shared laptop already hosts both models. |
| Matching with local `dotnet run` | Load the key in the terminal that starts Matching and set the tunnel URL in its local appsettings. |
| The AI API and models | Load the key in the terminal that starts the laptop API and run ngrok on that device. |
| A client of an already configured Matching cluster | The cluster already holds the key. You can call the existing Matching endpoints. |

### 1. Load the key in your terminal

Paste the shared value at the hidden prompt. These commands set the key for the current terminal and its child processes. Repeat this step in a new terminal when starting the API or running a direct probe. Keep the shared value in the password manager and out of Git.

**macOS — Terminal (zsh)**

```zsh
IFS= read -rs 'AiRuntime__ApiKey?Shared AI API key: '
printf '\n'
export AiRuntime__ApiKey
```

**Windows — PowerShell**

```powershell
$secret = Read-Host "Shared AI API key" -AsSecureString
$env:AiRuntime__ApiKey = [System.Net.NetworkCredential]::new("", $secret).Password
```

### 2. Configure the process that uses the key

**Matching in Kubernetes.** Start with a working [local Kind cluster](../../deploy/kubernetes/README.md) and the `smartlost` namespace. In [the Matching ConfigMap](../../deploy/kubernetes/base/matching/configmap.yaml), set `AiRuntime.BaseUrl` to the HTTPS origin shown by ngrok, for example `https://your-tunnel.example`. Run the block for your OS in the terminal from step 1. It creates or updates Secret `matching-ai-runtime`, entry `api-key`, preserving the exact bytes without a trailing newline.

macOS (zsh):

```zsh
if [[ -n "$AiRuntime__ApiKey" ]]; then
  printf '%s' "$AiRuntime__ApiKey" |
    kubectl --context kind-smartlost-local -n smartlost create secret generic matching-ai-runtime --from-file=api-key=/dev/stdin --dry-run=client -o json |
    kubectl --context kind-smartlost-local -n smartlost apply -f -
else
  echo 'Load the shared API key first.'
fi
```

Windows (PowerShell):

```powershell
if ([string]::IsNullOrWhiteSpace($env:AiRuntime__ApiKey)) { throw "Load the shared API key first." }
@{
    apiVersion = "v1"
    kind = "Secret"
    metadata = @{ name = "matching-ai-runtime"; namespace = "smartlost" }
    type = "Opaque"
    data = @{ "api-key" = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($env:AiRuntime__ApiKey)) }
} | ConvertTo-Json -Depth 4 -Compress | kubectl --context kind-smartlost-local -n smartlost apply -f -
```

Expect `secret/matching-ai-runtime created`, `configured` or `unchanged`. For an existing Matching deployment on either OS, run these commands from the repository root to apply the edited URL and reload the key in both pods:

```sh
kubectl --context kind-smartlost-local -n smartlost apply -f deploy/kubernetes/base/matching/configmap.yaml
kubectl --context kind-smartlost-local -n smartlost rollout restart deployment/matching-service
kubectl --context kind-smartlost-local -n smartlost rollout status deployment/matching-service
```

For a first deployment, follow the [Matching deployment guide](../../deploy/kubernetes/README.md#matching-api-and-postgresql) after creating the Secret. The key persists across terminal sessions and pod restarts in that cluster. Recreate it if you recreate the cluster. To rotate it, restart the laptop API with the new value, update the Secret in each client cluster and restart Matching.

**Matching with local .NET.** Use the [Matching local development setup](../../src/Services/Matching/README.md#local-development). Set `AiRuntime.BaseUrl` in its local `appsettings.json`. Start Matching from the terminal in step 1 so it inherits `AiRuntime__ApiKey`.

**Laptop hosting the AI API.** Prepare the [DINO](../DINOv3-ViTL16-ONNX/README.md) and [Qwen](../Qwen3-VL-4B-Instruct-ONNX/README.md) CPU models and their local settings. From the repository root in the terminal from step 1, run `cd AIModels`. Restore with `dotnet restore AIModels.slnx --locked-mode` on Windows. On macOS, use `dotnet restore AIModels.slnx -p:NuGetLockFilePath=obj/packages.mac-local.lock.json` for the macOS-specific ONNX dependencies. Then, on either OS:

```sh
dotnet build AIModels.slnx -c Release --no-restore
cd SmartLost.Ai.Api
dotnet run -c Release --no-build -- --urls http://127.0.0.1:5050
```

The API locates each CLI in its sibling project's Release output. Published copies can set `AiRuntime:DinoAssembly` and `AiRuntime:QwenAssembly` to absolute DLL paths. In a second terminal on the laptop, run `ngrok http 5050` with the configured ngrok agent. Keep both processes running.

### 3. Check the key without loading a model

Open a terminal, repeat step 1 if needed, and replace the example origin with the team's actual HTTPS tunnel. On the AI laptop you can also test `http://127.0.0.1:5050/embeddings` directly. These probes intentionally send `text/plain`. The API authenticates the request before checking its content type.

macOS (zsh):

```zsh
printf 'X-Api-Key: %s\n' "$AiRuntime__ApiKey" | curl -sS -o /dev/null -w 'HTTP %{http_code}\n' -X POST -H @- -H 'Content-Type: text/plain' -H 'Content-Length: 0' https://your-tunnel.example/embeddings
```

Windows (PowerShell):

```powershell
"X-Api-Key: $env:AiRuntime__ApiKey" | curl.exe -sS -o NUL -w "HTTP %{http_code}\n" -X POST -H "@-" -H "Content-Type: text/plain" -H "Content-Length: 0" "https://your-tunnel.example/embeddings"
```

**415 means the key was accepted.** A 401 means authentication failed. If the laptop and tunnel probes return 415 but Matching receives 401, repeat the Secret update with the same key and restart the Matching pods. For a real Windows inference test, use `Invoke-RestMethod` with `-InFile`:

```powershell
$result = Invoke-RestMethod -Method Post -Uri "https://your-tunnel.example/embeddings" -Headers @{ "X-Api-Key" = $env:AiRuntime__ApiKey } -ContentType image/jpeg -InFile ".\test.jpg" -TimeoutSec 195
$result.embedding.Count
```

DINO returns 1,024 values for a successful request.

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

For the shared Kind cluster, the Deployment reads the key from Secret `matching-ai-runtime` and mounts optional ConfigMap `matching-ai-test-photos` on every replica. After the [shared API key setup](#shared-api-key-setup-on-macos-and-windows), choose a small demo JPEG under 1 MiB to fit the ConfigMap. From the repository root, with namespace `smartlost` already created:

```sh
kubectl --context kind-smartlost-local -n smartlost create configmap matching-ai-test-photos --from-file=10000000-0000-0000-0000-000000000001_10000000-f0f0-0000-0000-000000000001_1.jpg=/path/to/test.jpg
```

Set `AiRuntime.BaseUrl` in the JSON in [the Matching ConfigMap](../../deploy/kubernetes/base/matching/configmap.yaml) before running `./k8s.sh deploy matching`. Both pods then receive the same URL and key. If you change live settings or the Secret directly, restart the Deployment so the subPath-mounted settings and environment are refreshed. Larger images can use a read-only volume at the same directory. Use the [Matching requests](../../src/Services/Matching/SmartLost.MatchingService.Api/MatchingService.Api.http) for the seeded source listing. The HTTP mode reads mounted images until Listings implements photo download.
