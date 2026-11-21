---
name: catalyst
description: Use for OpenAPI or Swagger specs and the typed TypeScript API clients generated from them with Catalyst. Triggers: finding how to call an API endpoint, which parameters, body and response an operation has, listing or searching endpoints, what a spec change breaks, regenerating or checking a generated client, catalyst.config.json, keeping generated clients in sync with the backend, openapi.json / swagger.json / openapi.yaml.
---

# Catalyst

Catalyst turns OpenAPI specs (JSON or YAML, file or URL) into dependency-free TypeScript clients, and answers questions about an API in the names and types of that generated client.

## Look things up through the MCP tools

The `catalyst` MCP server is registered for you. Prefer its tools over the CLI, and never read the spec or the generated client by hand to learn how to call an endpoint.

1. `list_clients`: the clients configured in `catalyst.config.json` (name, source, output). Call it first; its names are the `client` values below. An empty list means no config.
2. `list_operations { client?, source?, filter?, tag?, method? }`: find an endpoint. Every word of `filter` must match.
3. `show_operation { operation, client?, source?, includeTypes? }`: the exact generated call, parameters, body, response and type declarations. `operation` is `"GET /items/{id}"`, an operationId or `client.get.itemsId`.
4. `show_type { name, client?, source?, depth? }`: one type and what it references.
5. `diff_spec { base, client?, source? }`: breaking and non-breaking changes against an old spec (`base` is a path, URL, `git:<ref>:<path>` or `git:<ref>`). It reports and never fails.
6. `generate_clients { client?, check? }`: regenerate the configured clients, or with `check: true` only compare.

Pick the API with `client` or `source`. With neither, the only configured client is used; with several, one is required.

## CLI fallback

Without MCP, use the same commands with `--json`, which prints one JSON document on stdout (`schemaVersion`, `command`, `success`, `exitCode`, `error { code, message }`, plus the payload):

```
catalyst api list --client Name --filter pets --json
catalyst api show "GET /items/{id}" --client Name --json
catalyst api schema Pet --depth 1 --json
catalyst api diff --base git:HEAD --client Name --json
catalyst gen openapi --json
catalyst gen openapi --check --json
```

`--source <path|url>` replaces `--client`; add `--insecure` only for a local HTTPS server with a self-signed certificate.

## Rules

- Generated files are output. Never edit them: change the spec or the config and regenerate with `catalyst gen openapi` (every configured client), or `generate_clients` with `client` for just one.
- Before calling work done, run `catalyst gen openapi --check` (or `generate_clients` with `check: true`). It writes nothing and fails when a client is out of date or missing.
- After a backend change, run `diff_spec` to see what breaks before regenerating.
- A single client without config: `catalyst gen openapi --source <spec> --output <file-or-folder> --class-name <Name>`. `--source` alone only validates.

## catalyst.config.json

Found by walking up from the working directory. Relative `source` and `output` paths resolve against the config file's folder.

```json
{
  "gen": {
    "openapi": {
      "skipPaths": ["/internal/"],
      "clients": [
        { "name": "PetStore", "source": "https://localhost:7036/openapi/v1.json", "output": "src/api", "insecure": true, "skipPaths": ["/admin/"] }
      ]
    }
  }
}
```

`name`, `source` and `output` are required. `output` is a folder (the file is named after `name`) or a `.ts` file path. `insecure` and per-client `skipPaths` are optional; the per-client list falls back to `gen.openapi.skipPaths`.

## Exit codes and errors

| Code | Meaning |
| --- | --- |
| 0 | Success (with `--check`: everything up to date) |
| 1 | A client failed, the spec is invalid, or with `--check` a client is out of date or missing |
| 2 | Usage error: unknown option, stray argument, bad option combination |
| 130 | Cancelled |

With `--json`, `error.code` is stable, for example `SOURCE_NOT_FOUND`, `PARSE_FAILED`, `SPEC_INVALID`, `CONFIG_INVALID`, `CLIENT_INVALID`, `OPERATION_NOT_FOUND` (with `suggestions`), `SCHEMA_NOT_FOUND`, `USAGE`. A failed MCP tool call carries the same code and message.
