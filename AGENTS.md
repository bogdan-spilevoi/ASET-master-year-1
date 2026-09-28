# Repository agent rules

These rules apply to the entire repository. Maintain the root [README](README.md) as the
accurate entry point for developers.

## CI implementation

- Keep CI/CD orchestration in GitHub Actions workflow YAML, using Actions and inline native
  shell steps. Do not introduce standalone CI helper scripts or another build framework.
- Use Coverlet for .NET coverage collection. Preserve the strictly-above-80% overall and
  changed-code gates, all-branch/per-commit checks, and existing security/test failure gates.
- Keep deployment inactive until real infrastructure and approval settings are configured.

## Before editing

1. Inspect the repository structure, application entry points, container files, CI workflows,
   test projects, and existing documentation.
2. Treat source code and configuration as the source of truth—not old README text.
3. Do not invent endpoints, commands, ports, services, deployment behavior, or completed features.

## README structure

Keep the following sections in this order. When an area has no implementation, state that
briefly instead of inventing content or presenting plans as completed work.

1. **Project title and concise purpose**
   - State what the project does, its main technologies, and whether it is a learning,
     production, internal, or public project.
2. **Architecture**
   - Explain the architectural style in plain language.
   - Include a small Mermaid diagram only if it makes dependencies clearer.
   - State dependency direction and boundaries.
3. **Services/modules**
   - Add a table listing each service/module, its responsibility, and links to its API,
     application, domain, and infrastructure documentation when present.
   - State ownership boundaries, especially database ownership.
4. **Shared libraries/building blocks**
   - List shared projects and their responsibilities.
5. **Local development and containers**
   - Document every Compose/Docker configuration file and its purpose.
   - Explain how to create local environment files safely from `.env.example`.
   - Provide copy-paste commands to validate, start, stop, and remove the stack when the
     corresponding configuration exists.
   - Clearly distinguish `stop`, `down`, and destructive volume removal.
   - Include locally exposed service URLs/ports only when they actually exist.
6. **Database and migrations**
   - Document which service owns which database.
   - Explain cross-service references versus physical foreign keys when relevant.
   - Include exact migration commands only if they work with the current project.
7. **API surface**
   - List currently implemented public endpoints in a compact table.
   - Mention Swagger/OpenAPI availability and environment restrictions.
   - Never list planned endpoints as current endpoints.
8. **Quality and CI**
   - Document the pinned SDK/runtime and important quality configuration.
   - Include the exact local formatting, build, and test commands used by CI.
   - Describe only the CI jobs that currently exist.
   - State branch-protection expectations if they are configured; distinguish a checked-in
     ruleset from protection actually enabled on GitHub.
9. **Development conventions**
   - Capture conventions enforced by the codebase, such as error handling, validation,
     naming, layering, command handling, and testing practices.
10. **Further reading**
    - Link to architecture docs, database schema, roadmap, contribution guide, API docs,
      and per-module READMEs when those documents exist.

## Documentation maintenance

- Update the root README whenever a new service, endpoint, Docker file, Compose file, port,
  test suite, CI check, or deployment workflow is added or changed.
- Update the affected module README in the same change. Create one when introducing a module.
- Remove outdated “to be added” or “coming soon” statements when implementation is complete.
- Keep examples secret-safe: use `.env.example` placeholders, never real values.
- Use relative Markdown links that work in GitHub.
- Keep the README concise and operational; link to deeper documents instead of duplicating
  large specifications.
- After editing, verify all commands, paths, endpoint routes, service names, and ports against
  the repository. State what was actually executed versus only inspected.
- Do not claim Docker, CI, Kubernetes, deployment, security scanning, or tests are complete
  unless the corresponding files and configuration exist. Distinguish configured workflows
  from successful runs and local verification from GitHub/cloud verification.

## README automation

These are requirements for any future README-writing automation; they do not require adding
an automation when none exists.

- If README updates are generated or performed by GitHub Actions, run that workflow only
  after a pull request has been merged into the protected `main` branch.
- Trigger it from a `push` to `main`, so the README reflects only the accepted, stable project
  state. Ensure the run corresponds to accepted merged work; do not use a PR event to write it.
- Never let the automation commit README changes to an open feature branch or pull request
  branch.
- The workflow may create a follow-up documentation commit on `main` only when README content
  actually changed. Avoid no-op commits and recursive documentation-update runs.
- If branch protection prevents the GitHub Actions bot from pushing to `main`, configure an
  approved bot bypass or have the workflow open a dedicated documentation pull request instead.
  A dedicated documentation PR is the permitted alternative to a direct `main` commit, not
  permission to modify an existing feature/PR branch. Do not weaken branch protection to make
  automation work.
