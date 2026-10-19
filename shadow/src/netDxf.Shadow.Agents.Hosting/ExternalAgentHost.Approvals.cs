using System.Text.Json;
using System.Text.Json.Nodes;

namespace netDxf.Shadow.AgentHost;

public sealed partial class ExternalAgentHost
{
    public void Reply(ExternalAgentReply response)
    {
        RequireObject(response.Result, settings.MaximumRequestBytes);
        if (response.RequestId is not { Length: > 0 and <= 128 }) throw new ArgumentException("Supply a pending request ID.");
        lock (sync)
        {
            if (!pending.TryGetValue(response.RequestId, out var request)) throw new InvalidOperationException("This agent request is no longer pending.");
            if (request.Cancellation.IsCancellationRequested || DateTimeOffset.UtcNow >= request.Expires)
            { pending.Remove(response.RequestId); request.Completion.TrySetCanceled(); throw new InvalidOperationException("This agent request has expired."); }
            var result = ValidateReply(request, response.Result);
            pending.Remove(response.RequestId); request.Completion.TrySetResult(result);
        }
    }
    private static JsonElement ValidateReply(Pending request, JsonElement result)
    {
        var clean = new JsonObject();
        if (request.Method == "session/request_permission")
        {
            if (!result.TryGetProperty("optionId", out var option) || option.ValueKind == JsonValueKind.Null) clean["optionId"] = null;
            else
            {
                var choice = RequiredString(result, "optionId");
                if (!request.Parameters.GetProperty("options").EnumerateArray().Any(o => RequiredString(o, "optionId") == choice))
                    throw new ArgumentException("Choose one of the offered permission options.");
                clean["optionId"] = choice;
            }
        }
        else if (request.Method == "item/tool/requestUserInput")
        {
            if (!result.TryGetProperty("answers", out var answers) || answers.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Supply answers keyed by the requested question ID.");
            var questions = request.Parameters.GetProperty("questions"); var expected = new HashSet<string>(StringComparer.Ordinal);
            var cleanAnswers = new JsonObject();
            foreach (var question in questions.EnumerateArray())
            {
                var id = RequiredString(question, "id");
                if (!expected.Add(id)) throw new ArgumentException("Agent questions must have unique IDs.");
                if (!answers.TryGetProperty(id, out var answer) || answer.ValueKind != JsonValueKind.Object
                    || !answer.TryGetProperty("answers", out var values) || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() != 1
                    || values[0].ValueKind != JsonValueKind.String || values[0].GetString() is not { Length: > 0 and <= 16384 } text
                    || string.IsNullOrWhiteSpace(text) || text.Contains('\0')) throw new ArgumentException("Answer each requested question once with bounded text.");
                if (question.TryGetProperty("options", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0
                    && !(question.TryGetProperty("isOther", out var other) && other.ValueKind == JsonValueKind.True)
                    && !choices.EnumerateArray().Any(c => RequiredString(c, "label", 16384) == text))
                    throw new ArgumentException("Choose one of the offered question options.");
                cleanAnswers[id] = new JsonObject { ["answers"] = new JsonArray(JsonValue.Create(text)) };
            }
            if (answers.EnumerateObject().Any(p => !expected.Contains(p.Name))) throw new ArgumentException("Answer only the requested question IDs.");
            clean["answers"] = cleanAnswers;
        }
        else if (request.Method is "item/commandExecution/requestApproval" or "item/fileChange/requestApproval")
        {
            var decision = RequiredString(result, "decision");
            if (decision is not ("accept" or "decline" or "cancel")) throw new ArgumentException("Choose accept, decline, or cancel.");
            if (request.Parameters.TryGetProperty("availableDecisions", out var choices) && choices.ValueKind == JsonValueKind.Array
                && !choices.EnumerateArray().Any(c => c.ValueKind == JsonValueKind.String && c.GetString() == decision))
                throw new ArgumentException("Choose one of the offered approval decisions.");
            clean["decision"] = decision;
        }
        else throw new InvalidOperationException("This server request is not supported by the Studio approval UI.");
        return JsonSerializer.SerializeToElement(clean, Json);
    }
    private async Task<JsonElement> AskAsync(State state, string method, JsonElement parameters, CancellationToken token)
    {
        if (method is not ("session/request_permission" or "item/commandExecution/requestApproval" or "item/fileChange/requestApproval" or "item/tool/requestUserInput"))
            throw new NotSupportedException("Unsupported agent server request.");
        RequireObject(parameters, settings.MaximumRequestBytes);
        if (method == "item/tool/requestUserInput" && (!parameters.TryGetProperty("questions", out var questions)
            || questions.ValueKind != JsonValueKind.Array || questions.GetArrayLength() is < 1 or > 3))
            throw new ArgumentException("Agent input requests require one to three questions.");
        if (method == "item/tool/requestUserInput")
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var question in parameters.GetProperty("questions").EnumerateArray())
            {
                if (!ids.Add(RequiredString(question, "id"))) throw new ArgumentException("Question IDs must be unique.");
                if (question.TryGetProperty("isOther", out var other) && other.ValueKind is not (JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null))
                    throw new ArgumentException("Question isOther must be a boolean.");
                if (question.TryGetProperty("options", out var choices) && choices.ValueKind != JsonValueKind.Null)
                {
                    if (choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() > 128) throw new ArgumentException("Question options must be bounded.");
                    foreach (var choice in choices.EnumerateArray()) _ = RequiredString(choice, "label", 16384);
                }
            }
        }
        var id = Guid.NewGuid().ToString("N");
        Pending request; CancellationTokenSource timeout;
        lock (sync)
        {
            if (current != state || state.Lifetime.IsCancellationRequested) throw new OperationCanceledException("The agent session ended.");
            if (pending.Count >= 32) throw new InvalidOperationException("Too many pending agent approvals.");
            timeout = CancellationTokenSource.CreateLinkedTokenSource(token, state.Lifetime.Token); timeout.CancelAfter(settings.ApprovalTimeout);
            request = new(state, method, parameters.Clone(), new(TaskCreationOptions.RunContinuationsAsynchronously), timeout.Token, DateTimeOffset.UtcNow + settings.ApprovalTimeout);
            pending.Add(id, request);
            Emit(state, "request", method, JsonSerializer.SerializeToElement(new { requestId = id, parameters }, Json));
        }
        using (timeout)
        try { return await request.Completion.Task.WaitAsync(timeout.Token).ConfigureAwait(false); }
        finally
        {
            lock (sync)
            {
                pending.Remove(id);
                Emit(state, "resolved", "host/requestResolved", JsonSerializer.SerializeToElement(new { requestId = id }));
            }
        }
    }
}
