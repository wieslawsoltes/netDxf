using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.AI;
using netDxf.Shadow.Mcp;

namespace netDxf.Shadow.Agents;

public sealed partial class AgentSession
{
    private const int MaximumCallReceipts = 16384;
    private const int MaximumCallReceiptBudget = 4 * 1024 * 1024;
    private readonly Dictionary<string, AgentCallReceipt> callReceipts = new(StringComparer.Ordinal);
    private int callReceiptBudget;

    // Store a digest, not another copy of potentially large tool arguments. Object
    // property ordering is immaterial; array order and exact numeric values remain.
    private static string CallSignature(string name, JsonElement arguments)
    {
        using var bytes = new MemoryStream();
        using (var writer = new Utf8JsonWriter(bytes))
        {
            writer.WriteStartArray(); writer.WriteStringValue(name);
            WriteCanonical(arguments, writer); writer.WriteEndArray(); writer.Flush();
        }
        return Convert.ToHexString(SHA256.HashData(bytes.GetBuffer().AsSpan(0, checked((int)bytes.Length))));
    }

    private static void WriteCanonical(JsonElement value, Utf8JsonWriter writer)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                { writer.WritePropertyName(property.Name); WriteCanonical(property.Value, writer); }
                writer.WriteEndObject(); break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) WriteCanonical(item, writer);
                writer.WriteEndArray(); break;
            default:
                // JsonElement writes numbers without converting through double/decimal,
                // which could conflate distinct large revision or object identifiers.
                value.WriteTo(writer); break;
        }
    }

    private string? RedactedCallSignature(string name, JsonElement arguments, string signature)
    {
        var json = arguments.GetRawText();
        var portableJson = redact(json);
        var portableName = redact(name);
        if (portableJson == json && portableName == name) return null;
        using var document = JsonDocument.Parse(portableJson);
        var portableSignature = CallSignature(portableName, document.RootElement);
        return portableSignature == signature ? null : portableSignature;
    }

    private static bool ValidReceiptId(string? value) => value is { Length: > 0 and <= 256 }
        && !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl);
    private static bool ValidSignature(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
    // Reserve space for both hashes even before credentials need redaction. This is a
    // conservative upper bound on serialized receipt bytes, including escaped IDs.
    private static int ReceiptBudget(AgentCallReceipt receipt) => JsonEncodedText.Encode(receipt.CallId).EncodedUtf8Bytes.Length + 256;

    private static McpToolResult HistoricalCallResult(AgentCallReceipt receipt) => new(receipt.IsError
        ? "A previous attempt with this call identifier was recorded as failed or uncertain. It was not executed again. Its original result was compacted; a mutation may have committed. Inspect current state before issuing a new operation."
        : "This call previously completed successfully and was not executed again. Its original result was compacted. This is a historical receipt, not a fresh verification; inspect current state for up-to-date values.", receipt.IsError);

    private McpToolResult ReportReceiptResult(FunctionCallContent call, McpToolResult result, bool cached = false, bool historical = false)
    {
        Emit("toolCompleted", call.Name, new
        {
            callId = call.CallId, name = call.Name, result = result.Text, isError = result.IsError,
            milliseconds = 0, cached, historical, executed = false,
        });
        return result;
    }

    private IReadOnlyList<AgentCallReceipt> ExportCallReceipts()
    {
        // Only the authoritative in-memory ledger supplies receipts. Workspace archives
        // are editable files and must never become an execution/deduplication authority.
        var exported = new Dictionary<string, AgentCallReceipt>(callReceipts, StringComparer.Ordinal);
        foreach (var call in messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>())
        {
            if (call.InformationalOnly || !exported.TryGetValue(call.CallId, out var receipt)) continue;
            var arguments = AgentJson.Element(call.Arguments ?? new Dictionary<string, object?>());
            var signature = CallSignature(call.Name, arguments);
            if (signature != receipt.Signature && signature != receipt.RedactedSignature) continue;
            // Redaction can replace a credential in the retained transcript. Preserve the
            // original execution hash, and include only a second digest to validate the
            // portable representation. Rejected ID reuse cannot replace either digest.
            var portable = RedactedCallSignature(call.Name, arguments, receipt.Signature);
            if (portable is not null) exported[call.CallId] = receipt with { RedactedSignature = portable };
        }
        return exported.Values.OrderBy(receipt => receipt.CallId, StringComparer.Ordinal).ToArray();
    }

    private void TrimCallResultCache()
    {
        var retained = messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>()
            .Select(call => call.CallId).ToHashSet(StringComparer.Ordinal);
        foreach (var id in completedCalls.Keys.Where(id => !retained.Contains(id)).ToArray()) completedCalls.Remove(id);
    }

    private sealed record RestoredCallReceipts(Dictionary<string, AgentCallReceipt> Receipts,
        Dictionary<string, (string Signature, McpToolResult Result)> Results, int Budget);

    private static RestoredCallReceipts ValidateCallReceipts(IReadOnlyList<AgentCallReceipt>? ledger, IReadOnlyList<ChatMessage> history)
    {
        var receipts = new Dictionary<string, AgentCallReceipt>(StringComparer.Ordinal);
        var exactResults = new Dictionary<string, (string Signature, McpToolResult Result)>(StringComparer.Ordinal);
        var budget = 0;
        if (ledger?.Count > MaximumCallReceipts) throw new ArgumentException("Checkpoint tool receipts exceed their count limit.");
        foreach (var value in ledger ?? [])
        {
            if (value is null || !ValidReceiptId(value.CallId) || !ValidSignature(value.Signature)
                || value.RedactedSignature is not null && !ValidSignature(value.RedactedSignature))
                throw new ArgumentException("Invalid checkpoint tool receipt identifier or signature.");
            var receipt = value with { Signature = value.Signature.ToUpperInvariant(), RedactedSignature = value.RedactedSignature?.ToUpperInvariant() };
            if (!receipts.TryAdd(receipt.CallId, receipt)) throw new ArgumentException("Duplicate checkpoint tool receipt identifier.");
            budget = checked(budget + ReceiptBudget(receipt));
            if (budget > MaximumCallReceiptBudget) throw new ArgumentException("Checkpoint tool receipts exceed their byte budget.");
        }

        var pending = new Dictionary<string, FunctionCallContent>(StringComparer.Ordinal);
        foreach (var content in history.SelectMany(message => message.Contents))
        {
            if (content is FunctionCallContent call && !call.InformationalOnly) { pending[call.CallId] = call; continue; }
            if (content is not FunctionResultContent result || !pending.Remove(result.CallId, out var original)
                || original.Name is "agent_finish" or "agent_plan") continue;
            var isError = result.AdditionalProperties?.TryGetValue("shadow:isError", out var flag) == true
                && (flag is true || flag is JsonElement { ValueKind: JsonValueKind.True });
            var text = result.Result as string ?? AgentJson.String(result.Result);
            // A stop can occur before dispatch reaches the rest of a model's batch.
            // ExecuteCallsAsync records paired cancellation results for those calls, but
            // absence from the authoritative ledger confirms they were never reserved.
            if (ledger is not null && !receipts.ContainsKey(result.CallId) && isError
                && text == "Tool cancelled before a result was recorded. Inspect current state before repeating any mutation.") continue;
            // These are explicit dispatch rejections, not execution receipts. In particular,
            // a changed-argument retry must not overwrite its original authoritative ID.
            if (isError && UnexecutedReceiptResult(text, ledger is null)) continue;
            var signature = CallSignature(original.Name, AgentJson.Element(original.Arguments ?? new Dictionary<string, object?>()));
            if (receipts.TryGetValue(result.CallId, out var prior))
            {
                if ((signature != prior.Signature && signature != prior.RedactedSignature) || isError != prior.IsError)
                    throw new ArgumentException("A checkpoint tool receipt conflicts with its retained transcript.");
            }
            else
            {
                // Schema-1 checkpoints predating the ledger can still reconstruct receipts
                // from their retained completed pairs. An explicit ledger must be complete.
                if (ledger is not null) throw new ArgumentException("A retained tool result is missing its authoritative checkpoint receipt.");
                if (!ValidReceiptId(result.CallId) || receipts.Count >= MaximumCallReceipts)
                    throw new ArgumentException("Invalid or excessive retained checkpoint tool receipts.");
                prior = new(result.CallId, signature, isError);
                receipts.Add(result.CallId, prior); budget = checked(budget + ReceiptBudget(prior));
                if (budget > MaximumCallReceiptBudget) throw new ArgumentException("Retained checkpoint tool receipts exceed their byte budget.");
            }
            exactResults.TryAdd(result.CallId, (prior.Signature, new(text, isError)));
        }
        return new(receipts, exactResults, budget);
    }

    private static bool UnexecutedReceiptResult(string text, bool legacy)
    {
        if (legacy && text == "A completed call identifier was reused with different arguments. No operation was executed.") return true;
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String
                && code.GetString() is "call_id_reused" or "receipt_capacity" or "invalid_call_id";
        }
        catch (JsonException) { return false; }
    }

    private void RestoreCallReceipts(RestoredCallReceipts restored)
    {
        callReceipts.Clear(); completedCalls.Clear();
        foreach (var entry in restored.Receipts) callReceipts.Add(entry.Key, entry.Value);
        foreach (var entry in restored.Results) completedCalls.Add(entry.Key, entry.Value);
        callReceiptBudget = restored.Budget;
    }
}
