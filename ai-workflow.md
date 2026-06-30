# AI Workflow

## AI Tools and Models Used

For local development, Ollama with llama3.1 was used. This model is small enough to run locally without a GPU but still capable of handling the prompt complexity. It supports the same prompt format and tool calling as the hosted GPT model.
The hosted gpt-4o-mini model uses a GitHub PAT. It was used sparingly for final testing after behavioral changes. This model handles the level of prompt complexity and table formatting well.

| Tool / Model | Purpose |
|-------------|---------|
| **OpenAI gpt-4o-mini** | Hosted LLM for assistant responses during development and final testing |
| **OpenAI gpt-4o** | Secondary hosted model for comparison and when gpt-4o-mini rate limits were hit |
| **Ollama + llama3.1** | Local LLM for offline development — small enough to run without a GPU, supports tool calling |
| **Microsoft.Extensions.AI** | AI SDK replacing Semantic Kernel — provides IChatClient abstraction over OpenAI-compatible APIs |
| **OpenCode** | Autonomous coding agent used to implement features, refactor code, fix bugs, and generate test files |

## AI-Generated Code

Roughly **70%** of the code was AI-generated (via OpenCode). Hand-written portions include:
- Initial project scaffolding and dependency setup
- Local LLM setup, and best models suitable for my use case
- Investigation of how to test LLM responses, including LLM-as-judge evaluation
- Data files (CSVs and JSON mappings)
- Configuration values in `appsettings.json`
- Adding multiple models in config and being able to pass the model profile for easy switching
- Final review and manual testing of edge cases

## Changes and Rejections from AI Suggestions

- **Generic CSV query tool → category-specific tools**: The AI initially built a single `query_csv` tool that let the LLM pick tables, columns, and filters. This gave the LLM too much freedom and produced inconsistent formatting. Replaced with 7 strongly-typed methods (`portfolio_overview`, `single_position`, etc.) that return fixed pipe-delimited tables.
- **Markdown tables → pipe-delimited plain text**: The AI originally used markdown table syntax (`| --- | --- |`). The LLM wouldn't consistently render this, so we switched to plain pipe-delimited rows with a header line, and let Spectre.Console handle console rendering.
- **Duplicated FX rate loading**: Both `CalcTool` and `PortfolioHelperTool` independently loaded `fx_rates.csv`. Consolidated into a single `CalcTool.ConvertValue()` method.
- **Scripted eval tests → LLM-as-judge**: The AI originally wrote eval tests with hardcoded scripted responses. These tested nothing — they were tautologies. Replaced with real LLM calls plus an LLM-as-judge pass that checks semantic equivalence against an expected response.

## Verification of Assistant Answers

1. **Unit tests** for tool logic — edge cases like zero holdings, pending allocations, written-off deals, partial exits, currency conversion.
3. **LLM-as-judge evals** — for each category query, the LLM's response is passed to a second LLM call that judges whether it matches the expected meaning. This catches semantic failures without relying on exact string matching.
4. **Manual testing** with both Ollama (local) and GitHub Models (hosted) to verify real-world behavior.

## Architecture

The application uses the most recent Agent SDK (Microsoft.Extensions.AI), replacing Semantic Kernel. All prompts and rules are in `Prompts\system.md` and handle the tone, format and routing.
Making the CSV data available as tools allows the user to ask about multiple companies, valuations, fees, and obligations in a single session. The agent routes queries to the appropriate tool and returns results in a consistent format. The agent answers questions based on the returned data — there is no hard coding of prompt > response, only the table format.

## Decisions

### Data access
The `PortfolioHelperTool` parses the CSV and returns results in a hard-coded table format. Reasons:
- LLMs tend to return data differently with every request — sometimes markdown table, sometimes plain text. Forcing the table format ensures consistency.
- Loading data into DataTables, SQLite, or EF Core would be overkill for a POC.
- Pre-loading all data on app start isn't worth the benefit — the data is small and performance impact is negligible.

### Calculations
A `CalcTool` was added using the built-in C# expression evaluator for simple math expressions (currency conversion, MOIC calculation). This reduces LLM calls and avoids mathematical hallucinations.

### Presentation
Tables returned in the console use Spectre.Console for consistent formatting. Tables are returned as pipe-delimited text internally, then rendered by the agent. Based on the investor's tech savviness level, the agent adjusts tone and detail, explaining terms like MOIC and carry for less savvy investors.

## Limitations

- Only tested with the provided dataset — not validated with larger amounts of data.
- More data would mean larger requests and may exceed context windows.
- No conversation history — sessions cannot be resumed after exiting the agent.
- The tests use the same csv data as the agent, this would need to change before starting to use real data.

## Next Steps (Given Another 8 Hours)

- Expand the eval suite with more adversarial scenarios (prompt injection, out-of-scope queries).
- Add lazy-loading / caching for CSV data to improve startup time.
- Explore using a smaller/faster judge model for the eval step.
- Add performance regression tests to catch latency and token-usage drift.
- Test with larger datasets to validate context-window handling.
- Add a CI pipeline to run the eval tests on every PR and commit, with a badge in the README.