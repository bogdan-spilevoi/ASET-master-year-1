# Design pattern presentation guides

Each folder contains one member's two assigned patterns, implementation links,
explanation points and an existing execution flow. The guides document existing
code and include no new application classes. Keep these folders within the repository
so their source links work; they also work when browsed on GitHub.

| Member | Patterns | Guide |
| --- | --- | --- |
| Member 1 | Command and Mediator | [member1/README.md](member1/README.md) |
| Member 2 | Decorator and Chain of Responsibility | [member2/README.md](member2/README.md) |
| Member 3 | Adapter and Builder | [member3/README.md](member3/README.md) |
| Member 4 | Repository and Unit of Work | [member4/README.md](member4/README.md) |
| Member 5 | Observer and Iterator | [member5/README.md](member5/README.md) |
| Member 6 | Strategy and Transactional Outbox | [member6/README.md](member6/README.md) |

Repository and Unit of Work are enterprise patterns; Transactional Outbox is an
architectural pattern. The remaining assigned patterns are GoF categories, including
framework/generated usage. Decorator and Chain of Responsibility describe different
aspects of the same pipeline. These classifications remain explicit in the guides.

The team assigns names to these presentation slots and selects a coordinator. The
[shared inventory and coordinator checklist](../DESIGN_PATTERNS.md) contain the full
classification, source evidence and limits on claiming individual implementation credit.

When communication or participating code changes, update the affected guide and
shared inventory in the same change. Preserve the distinction between implemented
code, framework behavior and unimplemented functionality.
