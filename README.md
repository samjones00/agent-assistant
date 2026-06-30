# EquiTie Investor Assistant

A multi-agent conversational assistant that answers investor queries against a portfolio of startup investments.

## Prerequisites

- [.NET SDK 10.0](https://dotnet.microsoft.com/download) or later
- **Hosted LLM:** GitHub PAT token with model access
- **Local LLM:** [Ollama](https://ollama.com) installed locally

## Getting Started

### Hosted LLM (default provider)

1. Set your API key in `appsettings.json` under the relevant provider
2. Run from the project root:

```
run.bat
```

The script builds and runs the app with the default LLM. Optional `--investor-id INV001` can be added to `dotnet run` directly for a non-interactive session.

### Local LLM (Ollama)

1. Install Ollama:

```
winget install --id Ollama.Ollama
```

2. Run from the project root:

```
run-ollama-local.bat
```

The script starts Ollama, auto-pulls `llama3.1` on first run, preloads the model, and runs the app.

### Alternate provider

Run directly with a positional provider argument:

```
dotnet run -- ollama
```

## Tests

Run unit and scripted tool tests (no external LLM credentials required):

```
dotnet test src/InvestorAssistant.Tests
```

LLM eval tests require a configured provider (Ollama or hosted). See [`ai-workflow.md`](ai-workflow.md) for details on the LLM-as-judge evaluation approach.

## Example Prompts

### Portfolio Overview
> "What do I own?" / "portfolio overview"

```
Your portfolio consists of investments in Forgecraft Robotics and Inferna AI,
with a total value of £273,777.81 and £221,940.14 respectively. The MOIC
ranges from 1.62x to 6.84x.

| Deal Id | Company Name | Round | Commitment | Units | Contributed | Current Value | MOIC | Latest Price | Valuation Date |
|---------|-------------|-------|-----------|-------|------------|--------------|------|-------------|---------------|
| DEAL001 | Forgecraft Robotics | Seed | 40000 | 17777.78 | 40000.00 | 273777.81 | 6.84x | 15.4 | 2025-09-25 |
| DEAL002 | Forgecraft Robotics | Series A | 35000 | 4487.18 | 35000.00 | 68950.01 | 1.97x | 15.366 | 2025-09-25 |
| DEAL003 | Forgecraft Robotics | Series B | 26000 | 1688.31 | 15600.00 | 27299.97 | 1.75x | 16.17 | 2026-03-31 |
| DEAL006 | Inferna AI | Series B | 137000 | 7405.41 | 137000.00 | 221940.14 | 1.62x | 29.97 | 2026-04-30 |
```

### Single Position
> "Show me Forgecraft" / "position in Inferna AI"

```
Position in Forgecraft Robotics

| Deal Id | Round | Units | Contributed | Current Value | MOIC | Latest Price | Valuation Date |
|---------|-------|------|------------|--------------|------|-------------|---------------|
| DEAL001 | Seed | 17777.78 | 40000.00 | 273777.81 | 6.84x | 15.4 | 2025-09-25 |
| DEAL002 | Series A | 4487.18 | 35000.00 | 68950.01 | 1.97x | 15.366 | 2025-09-25 |
| DEAL003 | Series B | 1688.31 | 15600.00 | 27299.97 | 1.75x | 16.17 | 2026-03-31 |
```

### Distributions
> "What distributions have I received?"

```
No distributions found.
```

### Obligations
> "What are my capital calls and fees?"

```
Capital Calls:

| Call Id | Allocation Id | Investor Id | Deal Id | Call Number | Call Date | Amount | Currency | Due Date | Status |
|---------|-------------|------------|--------|------------|----------|-------|--------|---------|------|
| CALL0001 | ALC0001 | INV001 | DEAL001 | 1 | 2022-03-15 | 40000.0 | USD | 2022-03-15 | Paid |

Fees:

| Fee Id | Allocation Id | Investor Id | Deal Id | Fee Type | Period | Fee Rate % | Basis | Amount | Status |
|--------|-------------|------------|--------|---------|------|-----------|------|-------|------|
| FEE0001 | ALC0001 | INV001 | DEAL001 | Structuring Fee | 2022 | 4.0 | Commitment | 1600.0 | Paid |
```

### Fees by Company
> "Fees on Forgecraft"

```
| Fee Id | Allocation Id | Investor Id | Deal Id | Fee Type | Period | Fee Rate % | Basis | Amount | Status |
|--------|-------------|------------|--------|---------|------|-----------|------|-------|------|
| FEE0001 | ALC0001 | INV001 | DEAL001 | Structuring Fee | 2022 | 4.0 | Commitment | 1600.0 | Paid |
```

### Valuations
> "How has Forgecraft valuation changed?"

```
| Company Name | Deal Id | Round | Share Price | Valuation Date | Company Valuation (M) |
|-------------|--------|-------|------------|---------------|---------------------|
| Forgecraft Robotics | DEAL001 | Seed | 2.5 | 2022-03-15 | 15.0 |
| Forgecraft Robotics | DEAL001 | Seed | 7.8 | 2023-11-20 | 46.8 |
| Forgecraft Robotics | DEAL001 | Seed | 12.0 | 2024-12-31 | 72.0 |
| Forgecraft Robotics | DEAL001 | Seed | 15.4 | 2025-09-25 | 92.4 |
```

### Account Statement
> "Show my account statement"

```
| Line Id | Investor Id | Date | Type | Deal Id | Amount | Currency | Reference Id |
|---------|-------------|------|------|--------|-------|--------|-------------|
| LN00001 | INV001 | 2022-03-15 | Capital Contribution | DEAL001 | -40000.0 | USD | CALL0001 |
| LN00002 | INV001 | 2022-03-15 | Structuring Fee | DEAL001 | -1600.0 | USD | FEE0001 |
```

### Help
> "What can you do?"

```
I can help you with:
- Portfolio overview - view all your investments
- Single company position - view details for a specific company
- Distributions - view distributions received
- Obligations - view capital calls and fees
- Fees by company - view fees for a specific company
- Valuations - view valuation history
- Account statement - view transaction history
```
