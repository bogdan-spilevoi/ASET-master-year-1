# BPMN models for three teams

Six members are divided into three teams of two. Each team has one BPMN process
model, an editable source and SVG/PNG previews. Member numbers are assignment slots;
replace them with actual names before submitting.

| Team | Members | Process | Guide |
| --- | --- | --- | --- |
| 1 | 1 and 2 | Authentication: registration and login | [Files and responsibilities](team1-authentication/README.md) |
| 2 | 3 and 4 | Publish a lost or found listing | [Files and responsibilities](team2-publication/README.md) |
| 3 | 5 and 6 | Retrieve matching suggestions | [Files and responsibilities](team3-matching/README.md) |

## Open and edit

1. Open [bpmn.io](https://demo.bpmn.io/) or [Camunda Modeler](https://docs.camunda.io/docs/components/modeler/bpmn/).
2. Open the team's `.bpmn` file. It includes BPMN 2.0 diagram layout information.
3. Edit and save the source, then refresh its SVG/PNG preview in the same change.

Solid arrows are sequence flows inside a process. Dashed arrows are messages between
participants. Gateways model alternative paths; boundary timers/errors interrupt the
attached activity. Team 3 also uses an expanded sequential multi-instance subprocess
to evaluate each candidate. See the [BPMN symbol reference](https://camunda.com/bpmn/reference/).

These are non-executable process models (`isExecutable="false"`). Conditions are
descriptive; `configuredTimeout` represents a future configured timeout, not a selected
duration or executable engine expression. No workflow engine is added to the project.
Auth reflects implemented backend behavior. Publication and matching integrations are
planned and are labeled accordingly in both source pools and previews.

## Validation

The three files were checked against the BPMN 2.0 XSD, parsed with bpmn-moddle 10.3.1
and passed bpmnlint 12.0.0 recommended rules with no errors or warnings. They imported
into bpmn-js 18.31.1 without warnings; SVG/PNG previews were rendered and visually
reviewed. Sequence connector routing was checked for crossings through unrelated
flow-node bodies. These checks validate the diagrams, not runtime service behavior.
The tools ran from a temporary folder; no application dependencies or CI jobs changed.

## Submission

- Each pair reviews its normal, alternative and failure paths and explains the BPMN choices.
- Record the two actual team members and their contributions in the team's README.
- Commit the editable `.bpmn`, preview files and README to GitHub.
- Update source and previews together when the modeled flow changes.

[Repository guide](../../README.md)
