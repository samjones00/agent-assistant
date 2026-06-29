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
│              │◀────│  - AIFunction tools (in-process)      │◀────│   OpenAI)   │
└──────────────┘     │  - PromptTemplate with Handlebars     │     └─────────────┘
                       │  - Personalisation Middleware         │
                       │  - CsvService + CalcService          │
                       └──────────────┬───────────────────────┘
                                      │ reads
                             ┌────────▼────────┐
                             │  CSV Data Store │
                             │  (11 CSV files) │
                             └─────────────────┘
```

Single-process .NET console app or minimal ASP.NET web app. The MAF agent orchestrates the
loop: receive prompt, select tool, call local C# function, feed results back to LLM, return answer.

## 3. Technology Stack

| Layer | Choice | Rationale |
|---|---|---|
| Runtime | .NET 10 / C# 14 | Latest LTS, MAF target |
| Agent framework | Microsoft Agent Framework 1.0 (`Microsoft.Agents.AI`) | Unified SK + AutoGen, native MCP support, C# first-class |
| LLM | Azure OpenAI GPT-4o (or OpenAI fallback) | Strong tool calling, fast, available on Azure |
| Tool registry | MAF `AIFunction` | Tools registered as C# lambdas, no external process |
| Prompt templates | Handlebars (via `Handlebars.Net`) | Lightweight, MAF-compatible, no-JSON templates |
| Data layer | CSV loaded in memory via `CsvHelper` | Simple, matches provided dataset; no DB needed for prototype |
| Personalisation | Custom middleware in agent pipeline | Reads investor profile, selects tone/depth template |
| DI / Hosting | `Microsoft.Extensions.Hosting` | Standard .NET host builder, integrates with MAF |

## 4. Component Design

### 4.1 Agent (Microsoft Agent Framework)

- `ChatCompletionAgent` configured with system prompt + tool definitions.
- Agent pipeline includes middleware for personalisation (injects investor profile into context).
- Tools registered as `AIFunction` instances wrapping local service methods.
- Session-scoped memory via `FileMemoryProvider` (agent-file-memory).

### 4.7 Personalisation Middleware

Runs before each LLM call. Loads investor profile from `investors.csv`, then computes derived signals:
- **Deal count** — count of active allocations for this investor
- **Top sectors** — join allocations → deals → companies, rank by commitment
- **Portfolio concentration** — % of total commitment in top sector

These signals + raw profile fields (`age`, `tech_savviness`) select the tone/depth template variant and inject into the system prompt. All personalisation is purely presentational — underlying numbers never change.

### 4.2 LLM Connector

- `AzureAIClient` or `OpenAIClient` registered via MAF's `IChatClient` abstraction.
- Model: `gpt-4o` — strong at numeric reasoning, tool selection, and following tone instructions.
- Fallback: OpenAI direct API if Azure unavailable.

### 4.3 Tool Registry

- Tools are registered as MAF `AIFunction` instances at startup via `ToolRegistration.cs`.
- Each tool is a pure C# function — no external process, no serialisation overhead.
- Functions are decorated with `[Description]` attributes so the LLM understands their purpose and params.

### 4.4 Tool Services

Two in-process service classes:

- **CsvService** — exposes `QueryCsvAsync(table, filters, columns)` and `GetSchemaAsync()`.
  Reads CSVs into in-memory SQLite (or DuckDB) for efficient querying. Returns row-level
  provenance (file path + line number) alongside each result set. Registered as a singleton.
- **CalcService** — exposes `CalculateAsync(expression)` for numeric operations (MOIC, multiples)
  that the LLM delegates rather than computing inline, plus `FxConvertAsync(amount, from_currency,
  to_currency)` that uses `fx_rates.csv` for multi-currency conversion at query time.

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
5. MAF agent invokes `AIFunction` → CsvService.QueryCsvAsync → returns CSV rows
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

### 5.2 Tool Calling

- Agent → `AIFunction` → CsvService / CalcService (in-process)
- Tool definitions registered at startup via `ToolRegistration.cs` using `[Description]` attributes.
- Each tool result includes row-level provenance (file, line number) for citation.
  The LLM is instructed to cite these (e.g. "allocations.csv line 42") inline in its
  answer for every number presented.

## 6. Prompt Templates

Templates are Handlebars files parametrised by investor profile. Examples:

- **Portfolio Overview**: "You are {{investor_name}}. Here is a summary of your portfolio..."
  — plain vs data-dense variant selected by `tech_savviness` score and derived deal count.
- **Position Detail**: "Your position in {{company_name}} across {{round_count}} round(s)..."
  — includes cost basis per round, blended share price. Handles multi-round aggregation
  (Forgecraft Robotics Seed + Series A + Series B) and similar-name disambiguation
  (Northpeak Analytics vs Northpeak Health) via explicit tool filters.
- **Obligations**: "Upcoming capital calls: {{capital_calls}}" — distinguishes commitment vs
  contributed, flags pending/unfunded allocations.
- **Fees**: "On {{deal_name}} you pay {{effective_mgmt_fee}}% management fee..." — compares
  effective rate to deal standard to show discount.
- **Valuations**: Shows mark history, distinguishes markups from markdowns; zero-value
  for written-off companies.
- **Distributions**: Gross vs net of performance fee, realised vs unrealised split
  (partial secondaries).
- **Statement**: Plain-language summary of signed cash flows (negative = out, positive = in),
  converted to reporting currency.
- **Personalisation**: "You invest mainly in {{top_sector}} with {{deal_count}} deals. Here
  is how your portfolio is performing..." — reflects portfolio shape and concentration.

Template variants per category: `plain` (Low tech_savviness, jargon explained), `balanced`
(Medium), `data_dense` (High, many deals, concise with tables). The underlying numbers are
identical across variants — only tone, depth, and framing change.

Edge cases from the dataset (multi-round companies, per-investor share-price discounts,
FX conversion, write-offs, down rounds, similar names, partial secondaries, zero-holdings
investors) are handled through prompt instruction and tool-calling patterns rather than
special-case code.

Templates live in `Prompts/{category}.hbs` and are loaded at startup by the template engine.

## 7. Tool Inventory

| Service | Tools | Purpose |
|---|---|---|
| CsvService | `query_csv`, `get_schema` | Query CSV dataset with filters and aggregation; returns row-level provenance |
| CalcService | `calculate`, `fx_convert` | Safe numeric eval (MOIC, sums, percentages) + FX conversion via fx_rates.csv |
| (Future) DocService | `get_document`, `list_documents` | Retrieve deal documents (out of scope for prototype) |

## 8. Configuration & Settings

```json
{
  "LLM": {
    "Provider": "AzureOpenAI",
    "Deployment": "gpt-4o",
    "Endpoint": "https://...",
    "ApiKey": "..."
  },
  "Data": {
    "CsvPath": "./data"
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
│   └── InvestorAssistant.Services/     # In-process tool services
│       ├── CsvService.cs
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
User              MAF Agent            LLM (gpt-4o)      CsvService          CalcService
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

## 11. Guardrails (Note)

Guardrails around investment advice, audit trail, and data protection are explicitly out of
scope for the prototype. They are addressed in the separate build-roadmap.md for production.

## 12. Prototype Roadmap

Prototype (this build — ~2-3 hours):

1. Scaffold MAF console app with tool registration
2. Implement CsvService (in-memory SQLite over CSVs)
3. Implement CalcService (safe expression eval + FX conversion)
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
