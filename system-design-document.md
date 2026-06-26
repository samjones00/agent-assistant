# Investor Assistant — System Design Document (Prototype)

## 1. Purpose & Scope

Build a conversational AI assistant for EquiTie investors that answers questions about their
portfolio using the provided CSV dataset. The assistant is personalised per investor (age, tech
savviness, portfolio shape) and must never expose another investor's data. Prototype scope is a
chat loop over the CSV data — no auth, no production infra.

## 2. Architecture Overview

```
┌──────────────┐     ┌──────────────────────────────────────┐     ┌─────────────┐
│  User (CLI   │────▶│  MAF Agent (Microsoft.Agents.AI)     │────▶│  LLM        │
│  or Web UI)  │     │  - ChatCompletionAgent                │     │  (Azure     │
│              │◀────│  - MCP Client (tool calling)          │◀────│   OpenAI)   │
└──────────────┘     │  - PromptTemplate with Handlebars     │     └─────────────┘
                      │  - Personalisation Middleware         │
                      └──────────────┬───────────────────────┘
                                     │ calls MCP tools
                            ┌────────▼────────┐
                            │  MCP Servers    │
                            │  - CSV Query    │
                            │  - Calculator   │
                            └────────┬────────┘
                                     │ reads
                            ┌────────▼────────┐
                            │  CSV Data Store │
                            │  (11 CSV files) │
                            └─────────────────┘
```

Single-process .NET console app or minimal ASP.NET web app. The MAF agent orchestrates the
loop: receive prompt, select tool, call MCP server, feed results back to LLM, return answer.

## 3. Technology Stack

| Layer | Choice | Rationale |
|---|---|---|
| Runtime | .NET 10 / C# 14 | Latest LTS, MAF target |
| Agent framework | Microsoft Agent Framework 1.0 (`Microsoft.Agents.AI`) | Unified SK + AutoGen, native MCP support, C# first-class |
| LLM | Azure OpenAI GPT-4o (or OpenAI fallback) | Strong tool calling, fast, available on Azure |
| MCP SDK | `ModelContextProtocol` v1.2+ | Stable NuGet, stdio/HTTP transport |
| Prompt templates | Handlebars (via `Handlebars.Net`) | Lightweight, MAF-compatible, no-JSON templates |
| Data layer | CSV loaded in memory via `CsvHelper` | Simple, matches provided dataset; no DB needed for prototype |
| Personalisation | Custom middleware in agent pipeline | Reads investor profile, selects tone/depth template |
| DI / Hosting | `Microsoft.Extensions.Hosting` | Standard .NET host builder, integrates with MAF |

## 4. Component Design

### 4.1 Agent (Microsoft Agent Framework)

- `ChatCompletionAgent` configured with system prompt + tool definitions.
- Agent pipeline includes middleware for personalisation (injects investor profile into context).
- Tools registered as `AIFunction` instances that delegate to MCP server calls.
- Session-scoped memory via `FileMemoryProvider` (agent-file-memory).

### 4.2 LLM Connector

- `AzureAIClient` or `OpenAIClient` registered via MAF's `IChatClient` abstraction.
- Model: `gpt-4o` — strong at numeric reasoning, tool selection, and following tone instructions.
- Fallback: OpenAI direct API if Azure unavailable.

### 4.3 MCP Client

- `McpClient` configured per server (stdio transport pointing to a local .NET process).
- Discovers tools (e.g. `query_csv`, `calculate_moic`) at startup via `ListToolsAsync`.
- Forwards LLM tool calls to the appropriate MCP server and returns results.

### 4.4 MCP Servers

Two MCP servers for the prototype:

- **CsvQueryServer** — exposes `query_csv(table, filters, columns)` and `get_schema()` tools.
  Reads CSVs into in-memory SQLite (or DuckDB) for efficient querying.
- **CalcServer** — exposes `calculate(expression)` for numeric operations (MOIC, multiples)
  that the LLM delegates rather than computing inline.

### 4.5 Prompt Template Engine

- Handlebars templates stored in `Prompts/` directory.
- Templates segmented by query type: portfolio_overview, position_detail, obligations, fees,
  valuations, distributions, statement.
- Each template receives the investor profile + tool results and renders the final assistant
  response with appropriate tone, depth, and context.

### 4.6 Conversation/Memory Store

- In-memory `ConversationHistory` per session (no persistence in prototype).
- Tracks last N turns for context. MAF's `FileMemoryProvider` optionally persists notes
  across turns within a session.

## 5. Data Flow

### 5.1 User Prompt → Agent Response

```
User: "What's my current portfolio value?"
  │
  ▼
1. CLI/Web app sends prompt to MAF Agent
  │
  ▼
2. Agent pipeline applies personalisation middleware:
   - Load investor profile from investors.csv (by investor_id)
   - Select tone (plain / data-dense) based on tech_savviness + age
   - Inject profile into system prompt
  │
  ▼
3. MAF agent sends prompt + tools to LLM (gpt-4o)
  │
  ▼
4. LLM decides to call query_csv(table="allocations", filters={investor_id}, columns=[...])
  │
  ▼
5. MAF agent invokes MCP client → CsvQueryServer → returns CSV rows
  │
  ▼
6. LLM receives tool results, may call additional tools:
   - query_csv("valuations") for current share prices
   - calculate("sum(...)") for totals
  │
  ▼
7. LLM generates final answer in the requested tone
  │
  ▼
8. Agent returns response to UI, cites source rows
```

### 5.2 Tool Calling via MCP

- Agent → MCP Client → stdio/HTTP → MCP Server (CsvQueryServer)
- JSON-RPC request/response over stdio
- Tool definitions advertised at connect time via `ListTools`
- Each tool result includes row-level provenance (file, line number) for citation

## 6. Prompt Templates

Templates are Handlebars files parametrised by investor profile. Examples:

- **Portfolio Overview**: "You are {{investor_name}}. Here is a summary of your portfolio..."
  — plain vs data-dense variant selected by `tech_savviness` score.
- **Position Detail**: "Your position in {{company_name}} across {{round_count}} round(s)..."
  — includes cost basis per round, blended share price.
- **Obligations**: "Upcoming capital calls: {{capital_calls}}"
- **Fees**: "On {{deal_name}} you pay {{effective_mgmt_fee}}% management fee..."
- **Personalisation**: "You invest mainly in {{top_sector}}. Here is how your portfolio is
  performing..." — reflects portfolio shape.

Templates live in `Prompts/{category}.hbs` and are loaded at startup by the template engine.

## 7. MCP Server Inventory

| Server | Tools | Purpose |
|---|---|---|
| CsvQueryServer | `query_csv`, `get_schema` | Query CSV dataset with filters and aggregation |
| CalcServer | `calculate` | Safe numeric eval (MOIC, sums, percentages) |
| (Future) DocumentServer | `get_document`, `list_documents` | Retrieve deal documents (out of scope for prototype) |

## 8. Configuration & Settings

```json
{
  "LLM": {
    "Provider": "AzureOpenAI",
    "Deployment": "gpt-4o",
    "Endpoint": "https://...",
    "ApiKey": "..."
  },
  "MCP": {
    "CsvQueryServer": {
      "Transport": "stdio",
      "Command": "dotnet run --project src/CsvQueryServer"
    },
    "CalcServer": {
      "Transport": "stdio",
      "Command": "dotnet run --project src/CalcServer"
    }
  },
  "Investor": {
    "DefaultInvestorId": "inv_001"
  },
  "Personalisation": {
    "TechSavvinessThresholds": {
      "Plain": [0, 3],
      "Balanced": [4, 7],
      "DataDense": [8, 10]
    }
  }
}
```

## 9. Project Structure

```
investor-assistant/
├── src/
│   ├── InvestorAssistant.App/          # Main console/web host
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   └── Prompts/                    # Handlebars templates
│   │       ├── portfolio_overview.hbs
│   │       ├── position_detail.hbs
│   │       ├── obligations.hbs
│   │       ├── fees.hbs
│   │       ├── valuations.hbs
│   │       └── distributions.hbs
│   ├── InvestorAssistant.Agent/        # MAF agent orchestration
│   │   ├── InvestorAgent.cs
│   │   ├── PersonalisationMiddleware.cs
│   │   └── ToolRegistration.cs
│   ├── CsvQueryServer/                 # MCP server: CSV queries
│   │   ├── Program.cs
│   │   └── CsvService.cs
│   └── CalcServer/                     # MCP server: calculations
│       ├── Program.cs
│       └── CalcService.cs
├── data/                               # CSV dataset (not committed)
│   ├── investors.csv
│   ├── portfolio_companies.csv
│   ├── deals.csv
│   ├── allocations.csv
│   ├── valuations.csv
│   ├── capital_calls.csv
│   ├── fees.csv
│   ├── distributions.csv
│   ├── statement_lines.csv
│   └── fx_rates.csv
├── docs/
│   ├── system-design-document.md
│   ├── Dataset Guide.md
│   └── build-roadmap.md
├── README.md
└── ai-workflow.md
```

## 10. Sequence Diagrams

### Basic Q&A Flow

```
User              MAF Agent            LLM (gpt-4o)      CsvQueryServer      CalcServer
 │                    │                    │                    │                 │
 │── "What's my ─────▶│                    │                    │                 │
 │  portfolio value?"  │── system prompt ──▶│                    │                 │
 │                    │   + tools           │                    │                 │
 │                    │                    │── query_csv ───────▶│                 │
 │                    │                    │  (allocations)      │── rows ────────▶│
 │                    │                    │◀── results ────────│                 │
 │                    │                    │── query_csv ───────▶│                 │
 │                    │                    │  (valuations)       │── rows ────────▶│
 │                    │                    │◀── results ────────│                 │
 │                    │                    │── calculate ────────│────────────────▶│
 │                    │                    │  (MOIC, totals)     │                 │── result ──▶│
 │                    │                    │◀── result ─────────│◀────────────────│
 │                    │◀── final answer ───│                    │                 │
 │◀── response ──────│                    │                    │                 │
```

## 11. Prototype Roadmap

Prototype (this build — ~2-3 hours):

1. Scaffold MAF console app with MCP client/server
2. Implement CsvQueryServer (in-memory SQLite over CSVs)
3. Implement CalcServer (safe expression eval)
4. Wire agent: system prompt, tool registration, personalisation middleware
5. Write Handlebars prompt templates (3-4 query types)
6. End-to-end test with 3 investor scenarios
7. Write README, ai-workflow.md, build-roadmap.md

Full product (6-month plan):

| Phase | Month | Deliverables |
|---|---|---|
| P0 | 1-2 | Production API, auth (Entra ID), persistent DB (PostgreSQL), hosted LLM (Azure OpenAI), eval suite |
| P1 | 2-3 | Multi-agent: specialist agents (portfolio, fees, documents), supervisor agent for routing |
| P2 | 3-4 | Proactive capabilities: capital-call reminders, fee alerts, document/KYC requests |
| P3 | 4-5 | Reporting engine: draft investor letters, quarterly summaries, personalised PDFs |
| P4 | 5-6 | Full iOS integration, human-in-the-loop for high-risk actions, audit trail, compliance |
