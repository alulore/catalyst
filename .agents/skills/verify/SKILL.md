---
name: verify
description: >
  Use before calling a user-visible change done: build catalyst, run the changed
  command against a real spec the way a user would, and report what you saw.
---

# Verify

Use this before calling a user-visible change done: a command, its output, a generated client, `--json`, exit codes or an MCP tool.

## Start

Build once, then run the built CLI so each command starts fast:

- `dotnet build`
- `dotnet bin/Debug/net11.0/Catalyst.dll <command>`

Work in a temp folder for anything catalyst writes, never in the repo. The specs in `Catalyst.Tests/Fixtures/Specs/` are real inputs: `petstore.json` for everyday checks, `edge-cases.json` for awkward schemas, `museum.yaml` for YAML, `spotify.json` and `enterprise.json` for size.

## Drive

Run the command you changed the way a user would, with the flags the change touches:

- Generate: `gen openapi --source Catalyst.Tests/Fixtures/Specs/petstore.json --output <temp>/pets.ts`, then read the file you changed the output of. Add `--check` and `--json` when those paths changed.
- Explore: `api list --source <spec>`, `api show "GET /pet/{petId}" --source <spec>`, `api schema Pet --source <spec>`.
- Diff: copy a spec to the temp folder twice, make a real change to one (remove an endpoint, rename a property), then `api diff --source <edited> --base <original>`. It should list that change as breaking or non-breaking.
- MCP: start `mcp` and write these lines to its stdin, keeping stdin open until the replies arrive. Replies are JSON lines on stdout; logs go to stderr.
  - `{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"verify","version":"1"}}}`
  - `{"jsonrpc":"2.0","method":"notifications/initialized"}`
  - `{"jsonrpc":"2.0","id":2,"method":"tools/list"}`, then a `tools/call` for the tool you changed.

## Done looks like

- The command exits with the code the README's exit-code table gives for that case.
- Generated TypeScript reads right for the case you changed. `dotnet test` also compiles every snapshot with `tsc --strict`.
- `--json` output is one document on stdout and nothing else.
- Error messages name the problem and the fix.

## Stop

Delete the temp folder. Make sure no `Catalyst.dll mcp` process is left running.

## Report

Say which commands you ran, against which spec, and what you saw: the exit code and the part of the output that shows the change. If you could not run it, say so plainly.
