# Excel migration (Phase 0 analysis)

The source is the "Gestor Financeiro Pessoal" workbook (`FinanceTracker_Template.xlsx`, PT-PT). It was
inspected read-only; it is never stored in this repository. Tests use a synthetic workbook with the same
layout (`backend/tests/Integration.Tests/Fixtures/SyntheticWorkbook.cs`).

## Sheets

| Sheet | Role |
|---|---|
| `📊 Dashboard` | Six KPI tiles and a 12-month table; a pure mirror of `Resumo Anual` |
| `📅 Resumo Anual` | Per-month roll-up (rows 4–15), totals (row 16), cumulative and "real %" columns (L–O), two charts |
| `Jan` … `Dez` | Identical input sheets |
| `⚙️ Configurações` | Allocation percentages, expense categories, allocation statuses |

## Inputs per month sheet

| Cells | Meaning | Becomes |
|---|---|---|
| `B5`, `B6` (+ notes `C5:C6`) | Salary, other income | `Income` transactions (salary / other-income) |
| `D11`–`D14` | Actual Stocks/ETFs, Crypto, Travel, Other savings | `InvestmentContribution` / `Savings` in the matching bucket |
| `F11`–`F14` | Feito / Por fazer / Parcial / N/A | `AllocationCheck` |
| rows 20–30 (`A` desc, `B` category, `C` amount, `D` debit day) | Fixed expenses | `Expense`, nature Fixed, dated on the debit day |
| rows 35–46 (`A` category, `B` desc, `C` amount, `D` date) | Variable expenses, one aggregate line per category | `Expense`, nature Variable |

## Configuration

`B5` Stocks/ETFs 25 %, `B6` Crypto 0 %, `B9` Travel 5 %, `B10` Other savings 5 %, expense budget
`B13 = 1 − B7 − B11`. Imported as a budget version effective from January of the chosen year with the
expense pool in `Remainder` mode. Categories `B17:B40` map to built-in categories (editable in the wizard):

| Workbook | Category |
|---|---|
| Habitação | housing |
| Eletricidade / Água / Gás / Internet / Telemóvel | electricity / water-gas / internet / mobile (under Utilities) |
| Seguro de Saúde / Seguro Automóvel | health-insurance / car-insurance (under Insurance) |
| Ginásio | gym |
| Streaming / Subscrições | subscriptions |
| Supermercado | groceries |
| Restaurantes | restaurants |
| Transportes | transport |
| Lazer / Entretenimento | entertainment |
| Roupa | clothing |
| Saúde / Farmácia | health |
| Educação / Prendas / Viagens / Outro | education / gifts / travel / other |
| Extra 1–3 | other (remap as needed) |

## Workflow

1. **Analyze** — layout detected by sheet names and anchor cells (`A3 = RENDIMENTO`,
   `A18 = DESPESAS FIXAS`, `A33 = DESPESAS VARIÁVEIS`); unknown layouts are refused. Size, zip-bomb and
   macro checks. Formulas are not evaluated.
2. **Map** — year (the workbook has none), destination accounts, category mapping.
3. **Validate** — non-negative numbers, dates inside the month, known categories, allocations ≤ 100 %.
4. **Preview** — every staged row plus **reconciliation**: income, fixed, variable, invested and saved
   recomputed per month and compared with the workbook's cached `Resumo Anual` values to the cent.
5. **Import** — one database transaction; each row gets `source = Xlsx`, the import id, and
   `external_id = ft:<year>:<month>:<cell>`, so importing the same workbook again creates nothing.
6. **Undo** — removes exactly the rows of that import (audited).

Summary sheets are never imported: the application recomputes them and only uses them as the
reconciliation oracle.
