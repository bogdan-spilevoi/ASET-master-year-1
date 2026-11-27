# SmartLost web

The Angular application provides the campus report board, type/category filters, listing details, registration, sign-in and a form to upload a JPEG/PNG draft and publish it. The editor heading shows whether the item is Lost or Found throughout creation and when reopening a draft. The editor suggests the Qwen object type as the category and its broader class when available. The Category dropdown shows the full catalog even when a Qwen suggestion is selected. Choose another entry or select **Enter a category…** to edit the suggested name or type a new one. An empty catalog and empty suggestions open the text field directly. Reopening a saved draft restores the owner’s choice. Drafts keep the choice private. Publishing makes a new name available to everyone. The JWT stays in memory for the current page session. Reloading the page or reaching token expiry requires signing in again. Use **Save draft and return to profile** to keep partial edits before leaving the editor.

Signed-in users open **Profile** at `/profile` to see their username and email from Auth and their paginated reports from Listings. The dropdown selects All, Draft, Open, Found, Closed or Recovered and resets pagination. Selecting a draft card opens its saved fields, traits and uploaded photo. The editor reads the existing analysis status and requests only missing operations. The trash button asks for confirmation and removes the draft and its photo. The previous `/listings/mine` page redirects here. **Sign out** sends the in-memory refresh token to Auth for session revocation. After success, the client clears both tokens and returns to the public board. Found filters by report type across states. Recovered selects closed reports with a confirmed recovery.

Report cards load small photo previews as they approach the viewport. Opening a card displays the entire uploaded photo on the report page. Reports without an image remain readable.

## Photo analysis before editing a draft

After draft creation, the editor requests traits and an embedding for the uploaded photo. It reads the owner-only analysis status every three seconds between responses while work is pending. The editor shows the uploaded photo beneath an “Analyzing photo” overlay. Both operations must reach Ready before users can edit the report, select suggested traits or publish. Owners can type a custom trait and press Enter or Add trait to select it. Saving or publishing sends the checked traits, including manual entries. If a request or status check fails, the editor shows recovery controls. A terminal analysis failure keeps the form locked. Leaving the editor stops status checks; the backend owns accepted work.

## Run in the local cluster

From the repository root, use `./k8s.sh up` for a configured new cluster. With Auth and Listings already running, update only the web application:

```sh
./k8s.sh deploy web
./k8s.sh ui web
```

Open [SmartLost](http://localhost:4200). Keep the forwarding command running while using the site. The [Deployment and Service](../deploy/kubernetes/base/web/) run one Nginx replica in the smartlost namespace.

## Develop with live reload

With Auth and Listings reachable locally on ports 5080 and 5082, use Node 24.15+ in the 24.x series and npm. Stop the web port-forward first, or choose another dev port with `npm start -- --port 4201`.

```sh
cd frontend
npm ci
npm start
```

`npm run build` creates the production bundle in `dist/smartlost-web/browser/`. The [Dockerfile](Dockerfile) runs the same build from the lockfile and copies the compiled assets into Nginx. Use `frontend/` as its Docker build context.

## Source layout

| Directory | Responsibility |
| --- | --- |
| `src/app/core/auth/` | Session, protected report route and JWT interceptor |
| `src/app/features/auth/` | Login, registration and Auth HTTP client |
| `src/app/features/listings/` | Board, detail and report pages, Listings client and DTOs |
| `src/app/shared/` | Error feedback shared across features |
| `public/` | Static assets |

The [Nginx configuration](nginx.conf) and [development proxy](proxy.conf.json) route relative API requests. See the [communication diagram](COMMUNICATION.md) for upstreams and error handling.
