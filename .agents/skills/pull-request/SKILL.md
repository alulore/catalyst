---
name: pull-request
description: >
  Use when the user asks to open, create, prepare or submit a pull request, or
  when a branch is ready for review. Covers preflight, self-review, branch
  creation, the title and body, UI screenshots, and platform CLI creation for
  GitHub or GitLab, with a manual fallback.
---

# Pull request

Use this when the work is ready for review. Aim for a short, honest PR a
reviewer can read in one pass.

## Preflight

1. Confirm the repo, remote host, default branch and current branch. Stop if
   the remote is missing or unreachable.
2. Read `git status` and the full diff. Stop on unrelated work, secrets,
   generated files or build output.
3. On the default branch, create a `feat/` or `fix/` branch using the repo's
   prefixes.
4. Use the repo's package manager from `packageManager`, the lockfile or the
   scripts. If there is no signal, use `bun` when installed; otherwise ask. Run
   the `check` script or the checks in `AGENTS.md`/README, plus
   `agentfile audit .` when available. Run checks before committing.
5. If dirty, propose a commit message in the repo style and ask before
   committing. Never commit on the default branch. Add the `Verified:` trailer
   when the project's `commitGate` is on.
6. After any approved commit, stop if there is nothing ahead of the default.

## Self-review

Read the full diff. Confirm it matches the request and summary. Run
`git diff --check`. Look for secrets, debug code, commented-out code, generated
files, lockfile noise and unrelated edits. Do not split unless asked; mention
unrelated changes.

## Title and body

Match the repo's title style, for example `feat(auth): add password reset`.

Use this body shape:

- A short description of the core changes, two to four sentences.
- A bullet list of concrete changes only.
- For frontend changes, `## Screenshots` with `Before` and `After`.
- One short `Checks:` line.
- `Closes #...` only when an issue is known.

No Risks, Testing or Checklist sections unless asked.

## Create

Show the branch, title, body and checks. Ask before push and PR creation unless
the user already said to create it.

- Use `gh` for GitHub or `glab` for GitLab. If the CLI is missing or
  unauthenticated, print the branch, title, body and compare URL.
- GitHub: `gh pr create --base <default> --head <branch> --title <title>
  --body-file <file>`.
- GitLab: `glab mr create --source-branch <branch> --target-branch <default>
  --title <title> --description <body>`.
- Use a temp body file. Add `--draft` when checks fail or the user asks.
  Return the URL. Do not merge.

If the user wants the PR watched after creation, use the `babysit` skill.

If checks pass, create ready. If they fail, create a draft and name the
failures.

## Screenshots

Capture before and after with the `webapp-testing` skill when available,
otherwise the available browser tooling. Save them outside the repo and add
`## Screenshots` with `Before` and `After` placeholders.

GitHub has no safe CLI upload for private screenshots. After the PR exists,
open it and ask the user to drag the images in. Do not upload private UI to a
public or secret gist without permission. If the user asks to commit them, use
a clearly named folder and mention the tradeoff.

## Guardrails

- Never force push, merge or rewrite history.
- Never create a PR from the default branch or include secrets, `.env` files,
  build output or unrelated changes.
- Do not split the PR, or assign reviewers or labels, unless asked.
