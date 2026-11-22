---
name: workspace-hygiene
description: >
  End-of-task hygiene for any project: run the agentfile audit, resolve or
  flag drift, duplication and build artifacts, and leave the workspace clean.
---

# Workspace hygiene

Run this at the end of a task, before reporting done.

1. Run `agentfile audit .` from the project root, or pass the root if you are
   deeper. It checks drift, artifacts and duplication.
2. Report the audit summary; do not hide issues.
3. For each issue:
   - Build artifacts: delete only output you can regenerate and the project
     does not publish. Leave tracked paths, hand-written files or anything
     uncertain, and say why.
   - Drift or stale files: run `agentfile sync <project>`. Add `--force` when
     the overrides markers are gone or the manifest is invalid. Include the
     regenerated `agentfile.json`, `AGENTS.md`, `CLAUDE.md` and
     `.claude/skills/` in the task's commit if you are committing, or tell the
     user.
   - Code duplication: reuse a complete implementation, extract a shared helper
     when both sides are partial, or skip generated, test, short or intentional
     blocks. For a duplicated type, reuse it or derive one from the other
     (`extends`, `Pick`, `Omit`). For a copied block inside two functions,
     extract the lines into a helper both call. Refactor only when the shared
     idea is real. Mark intentional duplication with
     `// agentfile-dupes-ignore -- <why>` before a function or type, or
     `agentfile-dupes-ignore-start -- <why>` and `-end` around a region.
   - Possible duplicates (a note, not an issue): code or types that look alike
     but may mean different things. About a third are real. Merge one only
     when you are already changing that code and both sides mean the same
     thing; otherwise leave it.
   - Unmanaged project: ask before running `agentfile init`.
   - Anything else: follow the audit's command or ask.
4. Remove scratch files you created: temp scripts, screenshots, test output.
   Leave the project as you found it.
5. Never delete anything you cannot re-create.
6. If `agentfile` is missing, tell the user to install it through Toolkit (see
   the README). Do not silently skip hygiene.
