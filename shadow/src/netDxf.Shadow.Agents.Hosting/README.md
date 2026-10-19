# Agent companion hosting

`AgentHostApplication.Build(AgentHostOptions)` composes the authenticated loopback
provider bridge, browser MCP relay, optional granted workspace, and external agent
routes. It is independent of the sample executable. The browser MCP relay targets
the currently connected Studio document.

External agent executables and the workspace are configured by the application at
startup. `/agents/start` selects the enabled `codex` or `acp` profile; browser input
cannot select an executable, arguments, environment, working directory, MCP
server, sandbox, or approval policy. Codex receives the companion MCP bearer token
only through the child environment. ACP receives it through its in-memory HTTP
MCP configuration when the agent advertises HTTP support. Returned JSON, events,
and errors redact that companion credential. The companion never reads agent
credential stores. Managed sign-in remains the external agent's user-facing flow.

`/agents/call` exposes a method allowlist with reconstructed, bounded parameters.
Codex thread creation and resume require an explicit model and use
`unlessTrusted` approvals with the `workspaceWrite` sandbox. Text prompts are at
most 64 KiB; history uses pagination. ACP setup always supplies the granted cwd
and companion MCP configuration. ACP remains an external process with its own
execution policy; selecting a working directory is not an operating-system
filesystem sandbox.

Codex `turn/steer` accepts only `threadId`, `expectedTurnId` and bounded text
`input`. The server rejects a stale active-turn identity; the companion never
falls back to `turn/start`. Optional drawing references undergo the same current
document/occurrence validation as new prompts, while the expected turn remains
unchanged. `thread/compact/start` accepts only a thread ID. Its acknowledgement
does not indicate completion: the ordinary turn/item event stream reports
compaction progress and the terminal result. Neither operation accepts model,
workspace, sandbox or approval overrides.

`/agents/events?after=<sequence>` returns `protocol`, `status`, `connected`,
`sequence`, `truncated`, replay `events`, and current `pending` requests. Status is
`starting`, `connected`, `disconnected`, or `stopped`. Each event has `sequence`,
`kind`, `method`, and `data`. An acknowledgement from `turn/start` does not mean a
turn has completed; consume the agent's completion notifications. Replay has
both count and byte limits; oversized events carry explicit truncation metadata.
Use persisted history pagination when necessary.

`/agents/reply` accepts one pending request ID and an object result. Codex command
and file approvals allow only offered per-action decisions. Question replies
must answer exactly the requested IDs, once each, with an offered option unless
free text is enabled. ACP permission choices must be advertised by the agent.
Expiry, interruption, disconnect, and stop remove pending requests. Extra reply
fields are discarded before forwarding. Unknown callbacks fail closed.

`ExternalAgentHostSettings` is an optional server-side DI registration for finite
request/approval timeouts and byte limits. `/agents/stop` interrupts an unfinished
handshake as well as active calls and disposes the owned process. Starting and
stopping profiles serializes process ownership; callbacks and event polling stay
independent of in-flight agent calls.

Credential-free integration verification uses this project's actual HTTP routes
and dedicated fake child apphosts, without launching installed agents or signing
in:

```sh
dotnet run --project shadow/tests/netDxf.Shadow.ExternalAgents.Verification
```
