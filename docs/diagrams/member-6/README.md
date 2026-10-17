# Member 6: Azure deployment and local AI operation

Two English diagrams for Problem 2, based on the
[requirements analysis](https://docs.google.com/document/d/1AYPYDg9ku5bWQCnwcyzXk9HWsxV3w5WU/edit) and the
[Azure student architecture](../../AZURE_STUDENT_ARCHITECTURE.md).
They describe the **proposed semester demonstration environment**, with regional
backends/data in Switzerland North and local inference through an authenticated
tunnel. They do not certify deployed resources, active free benefits or a working
release workflow.

## Diagram previews

PNG exports supplied by the user and moved from Desktop without changing their bytes.

### UML Deployment

CI delivery, OIDC, Terraform state, registry, Key Vault, database bootstrap and private migration jobs.

![Member 6 UML Deployment diagram](images/smartlost-azure-deployment.png)

[Full-size PNG](images/smartlost-azure-deployment.png) · [Editable PlantUML](smartlost-azure-deployment.puml)

### C4 Deployment

Browser, Azure apps/databases, scheduled jobs and local Qwen/DINOv3 inference through an external HTTPS relay.

![Member 6 C4 Deployment diagram](images/smartlost-hybrid-deployment.png)

[Full-size PNG](images/smartlost-hybrid-deployment.png) · [Editable PlantUML](smartlost-hybrid-deployment.puml)

**Export note.** These deployment diagrams describe the proposed environment. They do not certify provisioned Azure resources or an operational local HTTP wrapper.

## Files and editing

| Diagram | Text source | Purpose |
| --- | --- | --- |
| UML Deployment | [PlantUML](smartlost-azure-deployment.puml) | Deployment nodes/artifacts, CI identity, infrastructure, secrets, images and private migration jobs |
| C4 Deployment | [C4-PlantUML](smartlost-hybrid-deployment.puml) | Runtime instances mapped to Azure and local hosts, traffic boundaries and finite processing jobs |

Copy the complete source into the
[PlantUML online editor](https://www.plantuml.com/plantuml/uml/).
The C4 source uses `!include <C4/C4_Deployment>` from the standard library.
The files are editable text; no PNG or draw.io export is required.

C4 Deployment is a **supplementary deployment view**, not level 4 Code. It maps
the containers from [Member 2](../member-2/README.md) onto execution environments.
The UML view concentrates on release infrastructure and artifact placement; the
C4 view concentrates on application operation. They are separate views of the
same proposed environment.

## UML deployment interpretation

Nested nodes represent hosting locations, execution environments or managed
resources. Artifacts represent images, the frontend distribution, workflow
definitions, state and stored configuration. Dotted dependencies identify who
delivers/configures what; they do not encode a sequence of workflow steps.

The intended release order remains the architecture document's order:

1. Verify shared resources, access, spare capacity, regional availability and
   active student benefits; use trusted-main OIDC and the protected environment.
2. Plan and apply reviewed Terraform with remote state locking. Read shared
   foundation resources as data sources in SmartLost's state.
3. Synchronise protected GitHub Secret values into Key Vault without logging
   them. Keep secret values out of application images and Terraform state.
4. Publish/promote immutable verified images from the accepted CI revision.
5. Execute service-owned migration jobs inside the shared private network; track
   their exact executions and fail the release if any migration fails.
6. Roll out the reviewed API digests and frontend distribution; verify cloud
   readiness and the critical hybrid flow, recording local AI status separately.

Entra federation needs an initial trusted bootstrap for this repository. It does
not grant access merely because SmartRemoteManager already has OIDC. Runtime/job
managed identities have narrowly scoped registry, Key Vault and Blob permissions.
Database credentials use distinct service runtime/migrator roles. Secret sync
does not itself rotate the corresponding database password.

A separate finite bootstrap job creates the SmartLost logical databases/roles
with restricted privileged credentials before service migrations. This is SQL
setup inside the private network, not an Azure Resource Manager operation that
creates PostgreSQL users. It must leave the other application's data/grants alone.

The migration job box represents **three service-specific job definitions**, each
using its own migration image and database grants. They are not one process with
routine access to all business databases. GitHub-hosted runners trigger them via
Azure management APIs and have no direct private database connection. Use both
workflow serialisation and database advisory locks. A failed release is still
failed after a successful image rollback; schemas are not automatically reversed.

Shared ACR, Container Apps environment/VNet and PostgreSQL retain their original
Terraform owner. SmartLost may add only its own apps/jobs, repositories, databases,
roles and scoped supporting resources. Never manage a shared resource from two
states or modify SmartRemoteManager's tables/grants. Backend storage reuse also
requires a unique SmartLost state key and authorised access.

The diagram groups API apps to keep the release view readable. The C4 view expands
each API and its database. Application database traffic, photo access, local AI
and scheduled processing are detailed there. The UML view does not claim a
GitHub runner can start or keep the operator computer online.

## C4 deployment interpretation

The Angular application executes in the user's browser after downloading its
static distribution from Static Web Apps Free. The static asset node is hosting
infrastructure, not a second Angular business service. Static content is globally
distributed; do not describe it as confined to Switzerland North. Validate the
supported Static Web Apps resource location during provisioning. Regional
backends and persistent application data target Switzerland North.

Browser calls go directly to public Auth/Listings HTTPS origins using explicit
CORS. The Free plan does not imply managed API linking. Matching has ingress
restricted to the shared Container Apps environment and authorises callers.
Listings validates JWTs locally and enforces ownership; business requests do not
require a per-request Auth callback. A public Listings origin still protects its
internal photo contract with service authentication.

Each API app initially targets **0.25 vCPU / 0.5 GiB, minimum 0 and maximum 1
replica**, subject to actual measurements. Scale-to-zero permits HTTP cold starts.
An HTTP request can wake an ingress-enabled app, but database polling inside a
stopped process cannot. Therefore two finite scheduled job definitions recover
durable work:

- **Listings synchronisation** claims Listings-owned tasks and sends revisioned
  updates to Matching, safely retrying failures and duplicate delivery.
- **Matching feature retry** claims Matching-owned work, fetches permitted photos
  through Listings and calls local inference through the same authenticated
  contract as Matching. It stores only version-compatible, revision-current
  results and uses backoff when the local computer is offline.

These are separate worker entry points for the responsibilities in
[Member 4](../member-4/README.md) and [Member 5](../member-5/README.md), not additional
business microservices. Warm API processes may also attempt work; atomic claims,
leases/recovery and idempotent processing must prevent duplicate execution from
creating duplicate effects. The initial architecture proposes a 15-minute retry
schedule with bounded batches; exact limits require measurement. Scheduled jobs
and database access are private and service-scoped, with no public job endpoints.

Three logical PostgreSQL databases share one eligible B1ms server. Separate
roles, ownership and migration histories preserve service boundaries. Logical
references cross service boundaries without physical cross-service foreign keys.
Connection pools must fit the shared server. Verify its actual major version
against Auth's PostgreSQL 16 integration fixtures before deployment.

Only Listings reads/writes original photo blobs. Matching and its feature job
request permitted photo bytes through Listings rather than using its database
or bypassing photo authorisation. Private blobs mean no anonymous photo access;
the diagram does not assert that Blob Storage or Key Vault has a private endpoint.
No new NAT gateway, API gateway, AKS cluster or AI VM is selected by these views.

The local runtime remains part of SmartLost, while the tunnel relay is an
external system. The local tunnel agent establishes an outbound session; the
relay arrow shows request delivery through that session, not an inbound router
port that CI opens. Only Matching's API/worker entry points call local inference.
The tunnel URL is configured separately from its service credential. Qwen/DINOv3
console prototypes exist; their HTTP wrapper, authentication and process/tunnel
startup are planned. No inference route or local port is invented here.

With the PC or tunnel offline, cloud accounts, listings, photos, recovery and
credibility remain available. Suggestions may use cached compatible vectors and
metadata; new AI work reports pending/unavailable and retries within limits.
Full AT03/AT06/AT10 acceptance still requires real local inference through the
tunnel, not only successful fallback.

## Requirements traceability

| Requirements / scenarios | Diagram elements |
| --- | --- |
| FR21, FR22 / UC12 | Azure hosting nodes, CI identity, reviewed artifacts, secret delivery and private migration jobs |
| FR04, NFR01, NFR02 | Protected API calls, internal Matching, scoped identities/roles and authorised photo access |
| FR07, FR14, FR20 / UC05, UC08 | Local runtime, external tunnel, versioned inference, cached ranking and offline outcomes |
| FR12, NFR03 | Durable service-owned work, finite processing jobs and safe retry after scale-to-zero |
| NFR08, NFR09 / AT10 | Identified releases, tracked migration executions and separate cloud/local readiness evidence |
| NFR10; decision D02 | Candidate sizing, student benefits, shared capacity and required runtime measurements |

## Student benefits and review scope

The architecture checks active 12-month ACR Standard and PostgreSQL allowances
before applying retail prices, plus the ongoing Container Apps grant and Static
Web Apps Free. Benefits and capacity are shared with SmartRemoteManager and do
not multiply per diagram, database or project. Reused infrastructure can still
incur charges; networking, logs and excess usage are not made free by app
scale-to-zero. These diagrams contain no promise of a zero bill or proven SLA.

Notation and hosting references:

- [PlantUML deployment syntax](https://plantuml.com/deployment-diagram)
- [C4 deployment views](https://c4model.com/diagrams/deployment)
- [C4-PlantUML deployment macros](https://github.com/plantuml-stdlib/C4-PlantUML/blob/master/C4_Deployment.puml)
- [Static Web Apps global hosting](https://learn.microsoft.com/en-us/azure/static-web-apps/faq#how-do-i-ensure-my-app-is-deployed-to-a-specific-azure-region)
- [Architecture, benefits and CI design](../../AZURE_STUDENT_ARCHITECTURE.md)
- [Deployment activation requirements](../../DEPLOYMENT.md)

Reviewed against the requirements, current architecture, existing diagram
contracts, Auth entry point/container configuration and current CI definitions.
Local links and PlantUML source structure were checked. User-provided PNG exports
were imported and visually inspected; sources were not re-rendered.
No workflow, Terraform, application endpoint, Azure resource or tunnel was added
or activated. No application tests, migrations, inference or cloud deployment
were run for this documentation change.
