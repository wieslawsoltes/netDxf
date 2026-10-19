# Opt-in agent CAD evaluation

This executable uses the reusable [Evaluation library](../../src/netDxf.Shadow.Agents.Evaluation/README.md) and measures opt-in real provider inference over the full local MCP catalog and independent native DXF checks. It is excluded from ordinary verification and never reads an API key unless `--live`, an explicit provider/model, verified conservative token prices, and a budget ledger are supplied. Supported providers are `openai` (`OPENAI_API_KEY`) and `anthropic` (`ANTHROPIC_API_KEY`). There are no embedded credentials, model defaults, model substitutions, provider retries, shell tools, or external input documents.

List the fixed creation, selective editing, and topology cases, or verify the native graders and cost guard without network access:

```sh
dotnet run --project shadow/tests/netDxf.Shadow.Agents.LiveEvaluation -- --list
dotnet run --project shadow/tests/netDxf.Shadow.Agents.LiveEvaluation -- --self-test
```

A live invocation requires values supplied by the operator after verifying the selected model's current rates. Load credentials into the process environment through your own secret manager; never put a key in arguments or a report:

```sh
dotnet run --project shadow/tests/netDxf.Shadow.Agents.LiveEvaluation -- \
  --live --provider openai --model "$EVALUATION_MODEL" \
  --input-usd-per-million "$INPUT_RATE" --output-usd-per-million "$OUTPUT_RATE" \
  --budget-ledger artifacts/shadow-agent-budget.json --max-cost-usd 20 \
  --case all --max-turns 12 --max-output-tokens 2048 --timeout-seconds 240
```

Use the **same ledger path and ceiling for every provider run** to share the ceiling. The runner rejects a ceiling above $20. It reserves a conservative full-input/output request cost on disk **before** each inference, including any context compaction inference, then reconciles reported OpenAI input/output usage at the supplied conservative rates. Anthropic reservations remain intact because the CLI does not assume a cache-accounting convention. Missing usage, failures, and interruptions retain the full reservation. An existing empty/damaged ledger is rejected, and exclusive file access prevents concurrent runs from spending the same remaining budget. Keep the ledger between runs; changing or deleting it defeats cross-run accounting. Rates must include any applicable cache-write or other token premiums; discounts are deliberately ignored. Unknown rates fail closed. This is a token-cost guard for these text/local-tool scenarios, not a guarantee against incorrect rates supplied by the operator or separate usage on the account.

Use `--repeats 1..10` for fresh isolated repeats sharing exactly the same frozen seed. `--cache-hints on|off` changes harness-added cache hints; neither choice proves a warm/cold cache or disables automatic provider caches. Repeated runs use separate numbered case directories.

Each isolated case produces `seed.dxf`, `drawing.dxf` (when serialization succeeds), `events.json`, `checkpoint.json`, and `report.json`; `summary.json` records all outcomes, selected provider/model, budgets, timings, token usage, tool calls/errors, undo transactions, requested cache settings, task and tool-catalog fingerprints, nullable cache/reasoning counts, and usage-completeness indicators. Exact task prompts are available through `--list` and retained checkpoints. Checkpoints contain the generated in-memory workspace, including graph `analysis.json`. Reports validate actual native entities, a save/reload round trip, bounded transaction counts, and successful real MCP calls. The graph case checks fixed expected counts and shortest-path length as well as the agent's saved analysis. Failures return exit code 1. A provider/configuration error stops further cases. Results characterize only the recorded prompts and model configuration, not a general model ranking.

Import `report.json` or `summary.json` into **Agents → Efficiency** to compare local observations. Failed attempts remain visible; unknown tokens and costs display as unknown. No evaluation results or improvement claims are fabricated. Google/Gemini and subscription APIs are deliberately unsupported by this executable and its single-request cost guard; the reusable target interface can record their externally supplied measurements without claiming an API bill.
