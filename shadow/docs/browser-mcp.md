# Browser MCP and ChatGPT site tools

Shadow Studio can expose the live drawing, shadow model, editor, workspace files, and Studio UI to another browser agent without running AgentHost. Open **Agents → MCP**, choose the agent permission policy in Settings, and enable **Browser MCP**. Disabling the control, switching drawings, or closing the workspace revokes its browser connections.

The page offers two companion-free transports:

| Transport | Client | Protocol |
| --- | --- | --- |
| WebMCP | A browser or browser agent that implements `document.modelContext`; a legacy `navigator.modelContext` implementation is also detected | The complete Studio tool catalog is registered in the top-level page. Each execution enters an official .NET MCP session. |
| MessagePort | A same-page integration, worker, or extension that receives an explicitly created `MessagePort` | MCP JSON-RPC messages, including initialization, tools, resources, prompts, ping, and cancellation, are handled by `netDxf.Shadow.Mcp` and the official MCP .NET SDK. |

Feature detection keeps the ordinary Studio UI and MessagePort integration available in browsers without WebMCP. WebMCP remains a developing browser API; the implementation follows the [Web Machine Learning Community Group draft](https://webmachinelearning.github.io/webmcp/) and unregisters its tools using the registration `AbortSignal`. Legacy implementations additionally receive `unregisterTool` calls for the names owned by Studio. It never clears unrelated page registrations or grants cross-origin tool exposure.

## Use ChatGPT's signed-in browser agent

In a supported ChatGPT desktop app, open Studio in the built-in browser, enable Browser MCP, and ask ChatGPT Work or Codex to inspect or edit the drawing. No provider credential is needed inside Studio when the external ChatGPT agent supplies the reasoning. Studio's **Read only**, **Approve changes**, and **Full access** policies still govern the tool calls.

OpenAI documents this integration as [Site tools](https://learn.chatgpt.com/docs/webmcp). Its current documentation specifies GPT-5.6 Sol or GPT-6 Sol, with availability depending on rollout and workspace policies. The browser discovers JavaScript tools in the top-level page and applies its own site-access and action review. Studio reports whether a compatible WebMCP API is available; registration does not guarantee that every browser or model can discover site tools.

This path uses the user's existing ChatGPT agent. It does not transfer subscription tokens into Studio's embedded agent harness.

## Connect an MCP client through MessagePort

After Browser MCP is enabled, same-page code can create a connection:

```javascript
const { port, close } = await globalThis.shadowStudioBrowserMcp.createConnection();
let nextId = 0;
const pending = new Map();
port.addEventListener('message', ({ data }) => {
  const request = pending.get(data.id);
  if (!request) return;
  pending.delete(data.id);
  if (data.error) request.reject(new Error(data.error.message));
  else request.resolve(data.result);
});
port.start();
function request(method, params = {}) {
  const id = ++nextId;
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve, reject });
    port.postMessage({ jsonrpc: '2.0', id, method, params });
  });
}

try {
  await request('initialize', {
    protocolVersion: '2025-11-25',
    capabilities: {},
    clientInfo: { name: 'My browser agent', version: '1.0.0' }
  });
  port.postMessage({ jsonrpc: '2.0', method: 'notifications/initialized' });
  console.log(await request('tools/list'));
} finally {
  await close();
  port.close();
}
```

`createConnection()` returns `{ port, close, sessionId }`. To attach an existing channel, call `shadowStudioBrowserMcp.connect(channel.port1)` and give `channel.port2` to the client. Connection creation requires Browser MCP to remain enabled for the same drawing.

For an integration that already uses the MCP JavaScript SDK, `agents/browser-mcp.mjs` exports `MessagePortMcpTransport`. Construct it with the granted port and pass it to the SDK client's `connect` method. The adapter implements `start`, `send`, `close`, `onmessage`, `onerror`, and `onclose`. Its `close` method sends the transport-specific `notifications/shadow/disconnect` notification before closing the port. The ordinary MCP protocol remains owned by the .NET SDK on the Studio side.

There is no automatic `window.postMessage` listener and no HTTP listener on GitHub Pages. The page capability must be explicitly granted through the Studio UI and passed through a same-page integration or trusted browser extension. A desktop MCP client that only accepts stdio or HTTP still needs a compatible transport or the optional AgentHost HTTP bridge.

## Permissions, cancellation, and limits

The MCP executor enters the same `AgentSession.ExecuteToolAsync` boundary as Studio's manual tool controls. It cannot bypass the selected permission policy, current drawing binding, tool schema validation, revision checks, or mutation receipts. Newly added tools or changed schemas require refreshing the explicit browser MCP grant. Tool activity and approval requests appear in the Agents panel. Metadata discovery can run concurrently; tool execution uses one ordered controller lane so independent browser clients cannot interleave state changes.

MCP `notifications/cancelled` reaches the actual tool's cancellation token. A WebMCP execution `AbortSignal` sends that targeted notification and rejects the caller promptly. Closing a session cancels its pending protocol work. Cancellation is not a rollback: a caller should inspect activity and current state before retrying an interrupted edit.

| Boundary | Limit |
| --- | --- |
| Active page MessagePorts | 4 |
| Underlying Studio browser MCP sessions | 8, including the WebMCP session |
| Request or response | 1,048,576 characters |
| String request ID | 256 characters |
| Retained request receipts | 4,096 requests, bounded by 8,388,608 response characters |
| MessagePort pending messages | 32; the .NET session also reserves bounded receipt space before dispatch |
| Protocol request deadline | 12 minutes, allowing the existing 10-minute approval window |
| Idle MessagePort lifetime | 15 minutes |

A repeated request with the exact same ID and JSON returns its stored receipt. Reusing the ID with different JSON is rejected. Completed mutations are never replayed after a lost reply. Request IDs and receipts are scoped to one explicitly opened session; clients must inspect state after reconnecting.

The browser MCP globals contain connection methods and capability status. They expose no provider configuration, API key, OAuth token, or hidden credential input. Credentials continue to follow the embedded harness's memory-only policy.

## Embedded ChatGPT plan sign-in

API-key providers can run in the browser independently of this integration. ChatGPT plan OAuth has a separate platform requirement. As checked against the official documentation on 2026-10-18:

- The [open-source plan-usage flow](https://developers.openai.com/siwc/token-sharing-open-source/sign-in) requires an HTTP `127.0.0.1` callback listener. Only the callback port may change between sign-ins.
- The [website sign-in flow](https://developers.openai.com/siwc/website) documents backend session handling and identity-only public clients. It does not provide a static-browser replacement for the open-source plan-usage flow.
- The [plan-usage overview](https://developers.openai.com/siwc/token-sharing-open-source) directs remotely hosted or paid integrations to a separate access process.

The embedded harness retains its supported ChatGPT subscription sign-in through the optional local companion. The WebMCP path above lets a supported signed-in ChatGPT browser agent operate Studio without that companion.
