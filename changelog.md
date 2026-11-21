# catalyst changelog

## 0.7.0 — 2026-11-21
### Native build, installed through toolkit
Catalyst is now a native Windows executable that installs and upgrades through toolkit.
+ Native AOT build: a much smaller download and about twice as fast to start
+ Installs with toolkit install catalyst, or irm https://downloads.alulore.com/tools/catalyst.ps1 | iex
+ The MCP server and the catalyst skill are registered automatically for OpenCode, Claude Code and Codex
~ Windows only: the Linux and macOS binaries are gone
~ The catalyst-cli .NET tool package is no longer published; install through toolkit instead

## 0.6.1 — 2026-10-22
### Upper-case HTTP methods
Generated clients now send the HTTP method in upper case.
! Generated TypeScript requests emit upper-case HTTP methods

## 0.6.0 — 2026-10-19
### Agents and API lookups
Catalyst can now answer questions about an API, compare specs, and serve agents over MCP.
+ catalyst mcp runs an MCP server with list_clients, list_operations, show_operation, show_type, diff_spec and generate_clients
+ catalyst api list, show and schema look up operations and types in the names of the generated client
+ catalyst api diff reports breaking and non-breaking changes against an old spec
+ --check compares generated code with the files on disk, and --json prints one JSON document for any command
~ Generated method names are always camelCase
~ Unknown options and stray arguments are usage errors that exit with code 2
~ Content types, YAML specs and the TypeScript generator handle more real-world specs
! Escape codes no longer leak into parsed output lines on CI

## 0.5.0 — 2026-10-12
### Every configured client in one run
Catalyst now generates all the clients a repository declares.
+ catalyst gen openapi without --source generates every client in catalyst.config.json
