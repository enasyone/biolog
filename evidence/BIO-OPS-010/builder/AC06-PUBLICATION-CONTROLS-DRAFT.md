# BIO-OPS-010 AC06 controls draft — not applied

This plan is intentionally inert. GitHub organisation name, repository name, teams, and handles must be confirmed after human approval.

## Branch protection

Protect the default branch and `develop`:

- require pull requests, two approvals, and a Code Owners approval;
- dismiss stale approvals and require approval of the latest reviewable push;
- require resolved conversations and strict/up-to-date status checks;
- block force pushes and deletion; apply rules to administrators;
- restrict direct pushes to the release integration role;
- require linear history if the selected merge strategy supports it;
- require signed commits prospectively after contributor signing is configured.

## Required continuous-integration checks

- pinned-toolchain .NET restore/build/unit/integration tests;
- web TypeScript and unit tests, plus critical Playwright journeys;
- Flutter analyse and tests;
- contract-freeze, traceability, and module-reference checks;
- gitleaks current-tree and full-history checks;
- dependency vulnerability and licence review with an explicit outage policy;
- evidence metadata validation binding results to the tested commit.

Workflows should use least-privilege `permissions`, pin third-party actions by full commit SHA, avoid secrets on fork pull requests, use environment approval for releases, and cancel superseded runs by concurrency group.

## Draft CODEOWNERS routing

Replace role placeholders with approved GitHub teams/handles before applying:

```text
*                                      @biolog/maintainers
/.github/                              @biolog/programme @biolog/security
/governance/                           @biolog/programme @biolog/architecture
/contracts/                            @biolog/architecture @biolog/security
/docs/adr/                             @biolog/architecture @biolog/security
/api/                                  @biolog/backend
/api/**/Tenant*                        @biolog/backend @biolog/security
/api/**/Auth*                          @biolog/backend @biolog/security
/api/**/Credential*                    @biolog/backend @biolog/security
/api/**/Biometric*                     @biolog/backend @biolog/security
/web/                                  @biolog/web
/mobile/                               @biolog/mobile
/scripts/*secret*                      @biolog/security @biolog/programme
/.gitleaks.toml                        @biolog/security @biolog/programme
```

## Application order

1. Obtain explicit publication approval and establish the repository.
2. Map placeholders to verified teams with at least two maintainers.
3. Add workflows and CODEOWNERS through a reviewed pull request.
4. Observe successful required checks on that pull request.
5. Enable rulesets using the exact observed check names.
6. Test rejection of direct push, force push, missing approval, missing owner approval, and failing checks.
7. Export final ruleset/workflow evidence under BIO-OPS-010 acceptance evidence.
