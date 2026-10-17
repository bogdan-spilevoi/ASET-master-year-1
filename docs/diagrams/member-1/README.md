# Member 1 diagrams

Two English diagrams for Problem 2, based on the
[requirements analysis](https://docs.google.com/document/d/1AYPYDg9ku5bWQCnwcyzXk9HWsxV3w5WU/edit).
They describe the target application. Blue use cases belong to the semester MVP,
including Azure delivery; purple dashed elements describe future extensions.
The diagrams do not claim that the target services or cloud release already exist.

## Diagram previews

PNG exports supplied by the user and moved from Desktop without changing their bytes.

### UML Use Case

Actors, semester use cases and future extensions, including optional analysis and recovery credibility.

![Member 1 UML Use Case diagram](images/smartlost-use-case.png)

[Full-size PNG](images/smartlost-use-case.png) · [Editable PlantUML](smartlost-use-case.puml)

### C4 System Context

SmartLost, campus users, deployment operator and external systems, with future participants marked.

![Member 1 C4 System Context diagram](images/smartlost-system-context.png)

[Full-size PNG](images/smartlost-system-context.png) · [Editable PlantUML](smartlost-system-context.puml)

**Export note.** The supplied context PNG predates the tunnel relay. The current context source includes it; local AI remains part of SmartLost.

## Files and editing

### Text sources (PlantUML)

- [UML Use Case source](smartlost-use-case.puml)
- [C4 System Context source](smartlost-system-context.puml)

Copy the complete contents of either `.puml` file into the
[PlantUML online editor](https://www.plantuml.com/plantuml/uml/).
Edit the text to regenerate the diagram automatically. The context source uses
`!include <C4/C4_Context>` from PlantUML's standard library; use a renderer that
includes that library. See the official [PlantUML use case syntax](https://plantuml.com/use-case-diagram)
and [C4-PlantUML documentation](https://plantuml-stdlib.github.io/C4-PlantUML/).

The `.puml` files are the editable sources; `images/` contains the supplied PNG
previews used by the repository README. Automatic layout can differ between
renderers. The context source includes the local-AI tunnel revision described in
the export note above.

## UML interpretation

- Actors A01 to A09 follow the requirements document. All actor symbols remain
  outside the SmartLost system boundary; use cases remain inside it.
- Lost Item Owner and Finder specialise Registered User. The hollow triangle
  points toward Registered User. They inherit the common user goals, with
  ownership conditions still applying.
- Repeated A01 and A02 symbols represent the same actor, repeated to reduce
  long lines. They do not introduce extra account types or actors.
- UC05 extends UC04 when image analysis is requested. UC09 extends UC08 when
  the lost item owner chooses to review a suggestion. The arrows point from
  the optional extension to the base use case.
- UC10 includes UC10a Award credibility, refining the mandatory behaviour in
  UC10 and FR18. UC10 is the matched recovery scenario; manual closure without
  an eligible helper does not award points.
- UC13 is refined into UC13a Report a listing and UC13b Review reported content.
  The requirements scenario contains these two separate actor goals. Reporting
  and moderation occur at different times, so no include relationship is used
  to imply that a user performs a moderator's action.
- Sign in is a precondition for authenticated business goals, rather than an
  included use case attached to every authenticated operation.
- A solid association records participation; it does not encode a workflow,
  message direction or chronological sequence.

| Actor | Direct interactions and inherited permissions |
| --- | --- |
| A01 Visitor | UC01, UC02 and future UC14 |
| A02 Registered User | UC03, UC04, UC06, UC07, UC08, UC11; future UC13a, UC15, UC16 and UC17 |
| A03 Lost Item Owner | UC09 and UC10; common goals inherited from A02 |
| A04 Finder | UC08; common goals inherited from A02 |
| A05 Deployment Operator | UC12 |
| A06 Moderator | Future UC13b |
| A07 Campus Benefits Manager | Future UC16 as a supporting participant |
| A08 Campus Identity Provider | Future UC14 as a supporting system |
| A09 Notification Provider | Future UC15 as a supporting system |

UC05 is optional user participation within UC04. UC10a is internal included
behaviour and does not need a separate human actor.

## C4 interpretation

SmartLost is shown as one Software System. Campus User combines Visitor and
Registered User interactions and may act as Lost Item Owner or Finder. The
Deployment Operator participates in the operational release scope from UC12;
this does not imply a new student administration screen.

Photo Storage and the HTTPS Tunnel Relay are external dependencies. The relay
connects cloud Matching to the local AI runtime; local model code remains part
of SmartLost. Photo Storage is the Azure Blob Storage dependency selected for the
semester. Moderator, Campus Benefits Manager, Campus Identity Provider and
Notification Provider are marked as future scope. Local account registration
does not independently verify campus membership.

Internal frontend applications, microservices, databases, Qwen and DINOv3 are
inside SmartLost and are abstracted at this level. Their decomposition belongs
in container and component views. Azure compute resource topology belongs in
a deployment view.

The level and notation follow the official
[C4 System Context guidance](https://c4model.com/diagrams/system-context) and
[C4 notation guidance](https://c4model.com/diagrams/notation).

## Verification scope

The previews are user-provided exports imported without image edits; their
content and completeness were visually inspected. Local links and PlantUML
source structure were checked, and diagram names and identifiers were compared
with the requirements document. The current PlantUML sources were not re-rendered.
Application tests, Docker and cloud deployment were not executed for this
documentation change.
