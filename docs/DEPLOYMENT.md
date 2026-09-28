# Deployment extension points

Deployment is intentionally inactive. There is no cloud account, registry, Kubernetes cluster,
namespace or application to deploy yet. `docs/examples/deploy.yml` lives outside the workflow
directory, and every unimplemented step exits with a failure directly in the workflow YAML.
A missing deployment implementation must never produce a successful deployment.

CI already creates scanned images and CycloneDX SBOMs when service Dockerfiles exist. It retains
image archives as Actions artifacts, without registry push permissions. PR container builds
validate changes but must not deploy to trusted environments. After merge, a successful `main`
revision is the promotion source.

The intended release sequence is:

```text
successful main CI → publish immutable scanned images → verify release provenance
  → deploy staging → smoke tests → production environment approval
  → deploy identical image digests → health verification
  → rollback to recorded healthy digests if deployment/health fails → verify rollback
```

Before activating the template:

1. Choose the cloud provider, container registry and Kubernetes distribution/version, plus
   deployment tooling (Helm or Kustomize). Keep versioned manifests under `deploy/`.
2. Build .NET 10 Linux containers with multi-stage Dockerfiles, non-root users, small runtime
   images and pinned base-image digests. Declare resource requests/limits, readiness/liveness/
   startup probes, security contexts and restricted service accounts in Kubernetes manifests.
   Validate manifests against the selected Kubernetes version; none is assumed at bootstrap.
3. Add a trusted `main` publishing job after **`CI / Required`**. Push once with commit/digest
   identifiers, generate attestations and publish a manifest mapping each service to its image
   digest and SBOM. Never rebuild between staging and production.
4. Implement the release-verification step to verify the manifest's registry, signatures/provenance,
   source repository, successful CI run, and commit reachable from protected `main`. A user-supplied
   digest by itself is not sufficient authorization to deploy.
5. Configure `staging`/`production` environments in GitHub with production required reviewers,
   self-review disabled, and `main`-only deployment. Confirm the repository's plan supports
   these controls. Configure least-privilege OIDC for each environment; avoid long-lived
   kubeconfig/cloud keys. Authentication must persist across the job's deployment steps.
6. Implement deployment steps with bounded rollout timeouts and smoke tests that exercise
   critical routes and dependencies after staging rollout. Verify health with bounded retries,
   ready-pod checks and external service probes; process start alone is not success.
7. Record the previously healthy release before production mutation. Implement the rollback
   step to restore that exact release after either rollout or health failure.
   Keep the original job failed even when rollback succeeds. Surface rollback failure for
   operator action. Use backward-compatible database migrations; image rollback cannot undo
   destructive database/schema changes. Handle first-deployment failure explicitly when no
   prior release exists.
8. Validate failure cases in staging: rollout timeout, smoke failure, unhealthy release,
   failed rollback and cancelled deployment. Configure independent cluster-level health
   monitoring/remediation because a cancelled or lost GitHub runner cannot guarantee rollback.
9. Copy `docs/examples/deploy.yml` to `.github/workflows/deploy.yml`, replace the authentication
   placeholders with the provider's pinned OIDC action, implement the workflow steps, and set
   repository variable `DEPLOYMENTS_ENABLED=true` only after review. The template supports manual promotion;
   automatic promotion may be added later from successful `main` CI with the same provenance gate.

The template serializes promotions (`cancel-in-progress: false`) and never deploys from a PR
workflow. Kubernetes API compatibility and service contract compatibility become additional
checks when manifests/contracts exist; today's compatibility matrix checks the .NET code across
Linux, Windows and macOS.
