# Deployment extension points

Deployment is intentionally inactive. This repository has no provisioned/verified Azure
infrastructure or active deployment workflow. The
[Azure student architecture proposal](AZURE_STUDENT_ARCHITECTURE.md) targets cloud
applications in Switzerland North and local AI through an authenticated tunnel,
with HTTP scale-to-zero and at most one month in the shared student subscription.
Active free benefits and shared capacity still require verification.
Kubernetes is not a selected requirement.

[Member 6's UML and C4 deployment diagrams](diagrams/member-6/README.md) show the
proposed CI infrastructure and Azure/local runtime placement as editable sources.

`docs/examples/deploy.yml` lives outside the workflow
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

1. Select the hosting resources, registry and deployment tooling after availability and
   budget checks. The student proposal is not provisioned infrastructure. If Kubernetes
   is selected instead, choose its distribution/version and Helm or Kustomize explicitly.
   Keep versioned deployment configuration under `deploy/`.
2. Build .NET 10 Linux containers with multi-stage Dockerfiles, non-root users, small runtime
   images and pinned base-image digests. Configure runtime limits, dependency readiness,
   startup/liveness checks and restricted identities for the selected host. Kubernetes
   manifests and version validation apply only if that hosting option is selected.
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
