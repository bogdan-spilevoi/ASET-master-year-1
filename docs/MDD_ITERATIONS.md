# Two MDD iterations

We used **Model Driven Development (MDD)**: define an Ecore model, generate C# with
NMF, then build and test the result. The second iteration extends the first.

## Iteration 1 Matching model

**Commit:** [29cc671 — 20 October 2026](https://github.com/bogdan-spilevoi/ASET-master-year-1/commit/29cc6710bf4d2038abca17f796e85d874a0ce37f)

**Goal:** represent the listing data and image vectors needed for matching.

| Step | Work completed |
| --- | --- |
| Model | Defined `SearchListing`, `ImageEmbedding` and their relationship in Ecore. |
| Generate | Used `NMF-Ecore2Code` to generate C# classes/interfaces and NMeta metadata. |
| Build | Added the Modeling and test projects to the solutions and built them. |
| Verify | Tested relationships, notifications and XMI round trips, including vector order and repeated values. |

**Recorded result:** 7 model tests passed, according to the
[verification recorded in that commit](https://github.com/bogdan-spilevoi/ASET-master-year-1/blob/29cc6710bf4d2038abca17f796e85d874a0ce37f/src/Services/Matching/SmartLost.MatchingService.Modeling/README.md).

## Iteration 2 Extend the models

**Commit:** [0867c7f — 26 October 2026](https://github.com/bogdan-spilevoi/ASET-master-year-1/commit/0867c7fdd01d3c9dd5a4d6e8fe5b92c2a0fa7384)

**Goal:** identify the current photo, represent pending embedding work, and model
Listings decisions, recoveries and credibility awards.

| Step | Work completed |
| --- | --- |
| Refine Matching | Added current-photo ID/revision and `EmbeddingPreparationTask` with processing status, attempts and retry/lease data. |
| Refine Listings | Added `MatchDecision`, `Recovery`, `CredibilityEntry` and a change sequence separate from content revision. |
| Regenerate | Regenerated both models' C# and NMeta files using NMF. |
| Verify | Added tests for the new fields, relationships, processing-state values and XMI round trips, plus a fictional confirmed-recovery XMI example. |

**Result:** Matching has three model classes; Listings has seven. The model tests
verify their structure and serialization. They do not execute matching, retries,
recovery transactions or point awards.

## Verification on 30 October 2026

The combined source at `b469d78` was checked; historical revisions were not rerun.

| Check | Result |
| --- | --- |
| Locked restore and root Release build | Passed; zero build warnings/errors |
| Regenerate both models into temporary output | Generated C# and NMeta matched the checked-in files |
| Matching model tests | 25 passed; 0 failed or skipped |
| Listings model tests | 28 passed; 0 failed or skipped |

TRX and coverage reports were generated locally under
`artifacts/tests/mdd-verification/`; they are ignored by Git.
Generation and test commands are in the
[Matching guide](../src/Services/Matching/SmartLost.MatchingService.Modeling/README.md)
and [Listings guide](../src/Services/Listings/SmartLost.ListingsService.Modeling/README.md).

## Architecture and remaining submission tasks

The architecture follows **Service-Oriented Architecture (SOA)**: Auth, Listings and
Matching have separate responsibilities and own their data. Cross-service communication
is designed around explicit contracts, without direct access to another service's database.

| Service | Data ownership |
| --- | --- |
| Auth | Accounts and authentication sessions |
| Listings | Original listings, photos, match decisions, recoveries and credibility records |
| Matching | Derived search data, embeddings and embedding-preparation records |

Auth is implemented as a runtime service. Listings/Matching service integrations remain
planned; their generated models are separate from runtime domain code.

Before submitting:

- Confirm that the laboratory coordinator can view the GitHub repository.
- Name the team coordinator and record each member's actual contribution and agreed points.
- Commit/push this report. Present both commit changes, the Ecore-to-C# generation
  process and the passing model tests.
