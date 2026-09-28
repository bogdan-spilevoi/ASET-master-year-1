# Activate repository protections

The connected GitHub integration reported `push: true` and `admin: false` for this private
repository. It cannot activate repository rules, secret protection or environment approvals.
No GitHub settings have been changed by adding these files.

## Main protection

1. Have a repository administrator review and push the initial infrastructure commit so
   `main` and the workflow exist. Let its first CI run complete.
2. In **Settings → Rules → Rulesets → New ruleset → Import a ruleset**, import
   [`.github/main-ruleset.json`](../.github/main-ruleset.json).
3. Confirm enforcement is **Active**, target is `main`, there are no bypass actors, and the
   required status check is **`CI / Required`** from **GitHub Actions**. The workflow job name
   and JSON context must remain synchronized. The GitHub Actions integration ID is `15368`.
4. The ruleset requires an up-to-date PR, one independent approving reviewer, approval of
   the latest push, dismissal of stale approvals, resolved review discussions, and passing
   CI. It blocks force-pushes and deletion. It permits squash/rebase merges.
5. Test with a disposable PR: a failing check must block merge and direct pushes to `main`
   must be rejected. A single contributor cannot approve their own PR; add a reviewer before
   enabling the one-approval requirement. Do not silently weaken the policy to work around it.

Alternatively, an administrator with GitHub CLI can import the reviewed ruleset once:

```sh
gh api --method POST repos/bogdan-spilevoi/ASET-master-year-1/rulesets \
  --input .github/main-ruleset.json
```

For updates, use the existing ruleset ID with `PUT`; do not create duplicates.
Private repository rulesets/protected branches require a supporting GitHub plan (GitHub Pro
for personal repositories or the appropriate organization plan). If the controls are unavailable,
the repository files cannot substitute for server-side enforcement.

## Actions and security settings

- Enable Actions; keep default workflow-token permissions **read-only**. These CI jobs request
  only `contents: read`, retain no checkout credentials, and do not access deployment secrets.
- Do not grant untrusted PRs cloud credentials, registry write access or privileged self-hosted
  runners. Use GitHub-hosted ephemeral runners for this pipeline.
- Enable Dependabot alerts and security updates in **Settings → Advanced Security / Code
  security**. Weekly version-update configuration is already committed.
- Enable **secret scanning and push protection** if available for the repository's plan.
  Gitleaks detects leaks in CI, including secrets later removed from files; native push
  protection can reject supported secrets before they enter the remote repository.
  Gitleaks by itself cannot reject a push before GitHub receives it.
- If a real credential is exposed, revoke/rotate it immediately; deleting the line does not
  invalidate the credential. Do not paste credentials into issues or scan output.
- Protect changes to `.github/`, build props and `coverage.runsettings` through normal PR
  review. Add CODEOWNERS once the actual maintainers/team handles are agreed.

## Later: environments

Create `staging` and `production` when deployment work begins. Restrict production to `main`,
configure required reviewers with self-review disabled, and use environment-scoped OIDC trust.
Approval availability also depends on the GitHub plan; a YAML `environment` name alone does
**not** configure an approval gate. Complete [the deployment checklist](DEPLOYMENT.md) before
enabling any workflow.

References: [GitHub rulesets](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/about-rulesets),
[protected branches](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-protected-branches/about-protected-branches),
[deployment environments](https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/manage-environments).
