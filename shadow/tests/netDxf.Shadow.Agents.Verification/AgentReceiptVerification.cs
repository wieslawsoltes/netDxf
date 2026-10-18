using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using netDxf.Shadow.Agents;
using netDxf.Shadow.Mcp;

internal static class AgentReceiptVerification
{
    internal static async Task<int> RunAsync()
    {
        var checks = 0;
        void Check(bool value, string description)
        {
            if (!value) throw new InvalidOperationException("Compacted receipt verification failed: " + description);
            checks++;
        }
        var options = new AgentOptions { PermissionMode = AgentPermissionMode.FullAccess, MaxRetries = 0, MaxOutputTokens = 1024, MaxTurns = 7 };
        var schema = McpJson.Parse("""{"type":"object","properties":{"label":{"type":"string"},"dimensions":{"type":"object","properties":{"width":{"type":"integer"},"height":{"type":"integer"}},"required":["width","height"],"additionalProperties":false}},"required":["label","dimensions"],"additionalProperties":false}""");
        foreach (var uncertainResult in new[] { false, true })
        {
            var mutations = 0; var reads = 0;
            var registry = new McpToolRegistry();
            registry.Register(new("mutate", "A change whose receipt must survive compaction", schema), (_, _) =>
            {
                mutations++;
                return ValueTask.FromResult(new McpToolResult(uncertainResult ? "The change may have committed; inspect state." : "The change committed.", uncertainResult));
            });
            registry.Register(new("read", "Inspect the next item", McpJson.Parse("""{"type":"object","properties":{},"additionalProperties":false}"""), true), (_, _) =>
                ValueTask.FromResult(new McpToolResult("Observed item " + ++reads)));
            using var client = new ScriptedChatClient();
            using var agent = new AgentSession(client, registry, options, ownsClient: false);
            client.EnqueueResponse(Call("persisted-write", "mutate", new { label = "fixture", dimensions = new { width = 2, height = 3 } }));
            for (var index = 0; index < 6; index++) client.EnqueueResponse(Call("receipt-read-" + index, "read"));
            await agent.RunAsync("Commit once, inspect the results, and retain the receipt through compaction.");
            Check(mutations == 1 && agent.Poll().Status == "paused", "the initial bounded run records exactly one attempted mutation");
            client.EnqueueResponse(new TextContent("One change was attempted; the recent inspection calls retain current context. Continue by inspecting recorded state."));
            await agent.CompactAsync();
            Check(agent.Transcript().SelectMany(message => message.Contents).All(content => content.CallId != "persisted-write")
                && agent.Workspace.List().Any(file => file.Path.StartsWith("agent-history/", StringComparison.Ordinal)),
                "compaction actually archives the original mutation's call/result pair");
            agent.Workspace.Write("keep.txt", "A malformed ledger must not replace or reversion this file.", 0);
            var checkpoint = agent.Export();
            var checkpointNode = JsonNode.Parse(checkpoint)!.AsObject();
            Check(checkpointNode["receipts"]!.AsArray().Any(item => item!["callId"]!.GetValue<string>() == "persisted-write"
                && item["isError"]!.GetValue<bool>() == uncertainResult), "the compacted checkpoint retains the mutation's success or uncertain-error receipt");
            if (!uncertainResult)
            {
                (string Description, Action<JsonObject> Apply)[] corruptions =
                [
                    ("ledger is not an array", value => value["receipts"] = new JsonObject()),
                    ("null receipt", value => value["receipts"]!.AsArray()[0] = null),
                    ("missing call ID", value => value["receipts"]!.AsArray()[0]!.AsObject().Remove("callId")),
                    ("empty call ID", value => value["receipts"]!.AsArray()[0]!["callId"] = ""),
                    ("overlong call ID", value => value["receipts"]!.AsArray()[0]!["callId"] = new string('x', 257)),
                    ("missing signature", value => value["receipts"]!.AsArray()[0]!.AsObject().Remove("signature")),
                    ("invalid signature", value => value["receipts"]!.AsArray()[0]!["signature"] = "not-a-sha256-signature"),
                    ("invalid redacted signature", value => value["receipts"]!.AsArray()[0]!["redactedSignature"] = "not-a-sha256-signature"),
                    ("non-boolean result status", value => value["receipts"]!.AsArray()[0]!["isError"] = "false"),
                    ("duplicate call ID", value => value["receipts"]!.AsArray().Add(value["receipts"]!.AsArray()[0]!.DeepClone())),
                    ("receipt count exceeds its bound", value => ExpandLedger(value, 16385, largeEntries: false)),
                    ("ledger bytes exceed their bound", value => ExpandLedger(value, 16384, largeEntries: true)),
                    ("signature conflicts with a retained successful call", value =>
                    {
                        var receipt = value["receipts"]!.AsArray().Single(item => item!["callId"]!.GetValue<string>() == "receipt-read-5")!;
                        receipt["signature"] = new string(receipt["signature"]!.GetValue<string>()[0] == 'A' ? 'B' : 'A', 64);
                    }),
                ];
                foreach (var corruption in corruptions)
                {
                    var candidate = JsonNode.Parse(checkpoint)!.AsObject();
                    corruption.Apply(candidate);
                    var rejected = false;
                    try { agent.Import(candidate.ToJsonString()); }
                    catch (Exception error) when (error is ArgumentException or JsonException) { rejected = true; }
                    Check(rejected && agent.Export() == checkpoint, "invalid ledger is rejected before changing transcript or workspace versions: " + corruption.Description);
                }
            }

            using var restoredClient = new ScriptedChatClient();
            using var restored = new AgentSession(restoredClient, registry, options, ownsClient: false);
            restored.Import(checkpoint);
            // Canonically equivalent arguments may arrive in a different property order after
            // an official SDK or the NDJSON bridge has serialized them again.
            restoredClient.EnqueueResponse(Call("persisted-write", "mutate", new { dimensions = new { height = 3, width = 2 }, label = "fixture" }));
            restoredClient.Enqueue((input, _, token) =>
            {
                Check(IsError(input, "persisted-write") == uncertainResult && mutations == 1,
                    "the restored receipt matches reordered arguments and preserves the original outcome without executing");
                return ScriptedChatClient.Response([Call("persisted-write", "mutate", new { label = "different", dimensions = new { width = 2, height = 3 } })], token);
            });
            restoredClient.Enqueue((input, _, token) =>
            {
                Check(IsError(input, "persisted-write") && mutations == 1, "reusing a compacted call ID for different arguments is rejected without a new mutation");
                return ScriptedChatClient.Response([new TextContent("Recorded state has been inspected; the change was not repeated.")], token, finish: true);
            });
            await restored.RunAsync("");
            Check(mutations == 1 && restored.Poll().Status == "completed", "compaction, export/import and continuation cannot replay a recorded mutation");
            using var secondClient = new ScriptedChatClient();
            using var secondRestore = new AgentSession(secondClient, registry, options, ownsClient: false);
            secondRestore.Import(restored.Export());
            secondClient.EnqueueResponse(Call("persisted-write", "mutate", new { label = "fixture", dimensions = new { width = 2, height = 3 } }));
            secondClient.Enqueue((input, _, token) =>
            {
                Check(IsError(input, "persisted-write") == uncertainResult && mutations == 1,
                    "a rejected changed-argument replay cannot replace the original receipt during another checkpoint roundtrip");
                return ScriptedChatClient.Response([new TextContent("The original receipt remains authoritative.")], token, finish: true);
            });
            await secondRestore.RunAsync("");
            Check(mutations == 1 && secondRestore.Poll().Status == "completed", "the checkpoint remains resumable after a rejected conflicting call ID");
        }

        // Redaction changes portable arguments, but may neither break import nor replace the
        // original execution fingerprint with a redacted-placeholder fingerprint.
        {
            const string secret = "fixture-redaction-secret-7bafe1";
            var mutations = 0;
            string Redact(string text) => text.Replace(secret, "[redacted]", StringComparison.Ordinal);
            var registry = new McpToolRegistry();
            registry.Register(new("redacted_change", "Change with a credential-bearing argument", schema), (_, _) =>
            { mutations++; return ValueTask.FromResult(new McpToolResult("The change committed.")); });
            using var client = new ScriptedChatClient();
            using var agent = new AgentSession(client, registry, options, redactor: Redact, ownsClient: false);
            client.EnqueueResponse(Call("redacted-write", "redacted_change", new { label = secret, dimensions = new { width = 2, height = 3 } }));
            client.EnqueueResponse(new TextContent("The change is complete."));
            await agent.RunAsync("Retain the execution receipt without exporting the credential.");
            var checkpoint = agent.Export();
            var receipt = JsonNode.Parse(checkpoint)!["receipts"]!.AsArray().Single(item => item!["callId"]!.GetValue<string>() == "redacted-write")!;
            Check(!checkpoint.Contains(secret, StringComparison.Ordinal) && receipt["signature"]!.GetValue<string>() != receipt["redactedSignature"]!.GetValue<string>(),
                "credential redaction exports a distinct portable validation fingerprint without the credential");
            using var restoredClient = new ScriptedChatClient();
            using var restored = new AgentSession(restoredClient, registry, options, redactor: Redact, ownsClient: false);
            restored.Import(checkpoint);
            restoredClient.EnqueueResponse(Call("redacted-write", "redacted_change", new { label = "[redacted]", dimensions = new { width = 2, height = 3 } }));
            restoredClient.Enqueue((input, _, token) =>
            {
                Check(IsError(input, "redacted-write") && mutations == 1, "portable redacted arguments cannot replace the original execution signature");
                return ScriptedChatClient.Response([Call("redacted-write", "redacted_change", new { label = secret, dimensions = new { width = 2, height = 3 } })], token);
            });
            restoredClient.Enqueue((input, _, token) =>
            {
                Check(!IsError(input, "redacted-write") && mutations == 1, "an original credential-bearing call still matches its receipt after redacted checkpoint import");
                return ScriptedChatClient.Response([new TextContent("The original mutation was not repeated.")], token, finish: true);
            });
            await restored.ResumeAsync();
            Check(restored.Poll().Status == "completed" && mutations == 1 && !restored.Export().Contains(secret, StringComparison.Ordinal),
                "redacted receipt recovery completes without credential disclosure or duplicate mutation");
        }
        return checks;
    }

    private static void ExpandLedger(JsonObject checkpoint, int count, bool largeEntries)
    {
        var ledger = checkpoint["receipts"]!.AsArray();
        while (ledger.Count < count)
        {
            var callId = "extra-receipt-" + ledger.Count;
            if (largeEntries) callId = callId.PadRight(256, 'x');
            var receipt = new JsonObject { ["callId"] = callId, ["signature"] = new string('A', 64), ["isError"] = false };
            if (largeEntries) receipt["redactedSignature"] = new string('B', 64);
            ledger.Add(receipt);
        }
    }

    private static FunctionCallContent Call(string id, string name, object? arguments = null) => new(id, name,
        McpJson.Element(arguments ?? new { }).Deserialize<Dictionary<string, object?>>(McpJson.Options));
    private static bool IsError(IReadOnlyList<ChatMessage> messages, string callId)
    {
        var result = messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>().Last(item => item.CallId == callId);
        return result.AdditionalProperties?.TryGetValue("shadow:isError", out var error) == true
            && (error is true || error is JsonElement { ValueKind: JsonValueKind.True });
    }
}
