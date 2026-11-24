# AudioYotoShelf

## Work tracking: GitHub Issues and Projects

This is a public open-source repo, so its work is tracked on GitHub, not in Vikunja. This overrides
the global rule that Vikunja is the only task tracker.

- **Tracker:** GitHub Issues on `O-Mutt/AudioYotoShelf`, using the repo's issue templates
  (`.github/ISSUE_TEMPLATE/`) and existing labels (`bug`, `enhancement`, `documentation`, …).
- **Board:** add each issue to the AudioYotoShelf GitHub Project, number 2
  (`gh project item-add 2 --owner O-Mutt --url <issue-url>`).
- **Branches:** `<type>/<issue-number>-<slug>`, with the types `.github/PULL_REQUEST_TEMPLATE.md`
  allows: `feature`, `fix`, `chore`, `docs`. Example: `fix/104-transcode-poll-retry`.
- **PRs:** follow `.github/PULL_REQUEST_TEMPLATE.md` and close the issue with `Closes #<number>`.
- **Follow-ups:** work a PR does not do gets an issue before the merge: a review finding you decline,
  a follow-up you notice, a step left for a person. Link the issue where you made the call.
- **Specs** stay in `docs/specs/`; keep issues short and link the spec page instead of repeating it.
- Creating or editing issues is outward-facing: draft them and confirm with the user before running
  `gh issue create`, unless told to go ahead.
