# NOTES

## Assumptions

- **Runtime:** The project targets **.NET 10** (the machine had only the .NET 10.0.5 runtime
  installed; the original `net8.0` target was updated across all three `.csproj` files).
- **Chrome version:** Tests run against Chrome 146. Selenium Manager auto-downloads the matching
  ChromeDriver, so no manual ChromeDriver installation is needed as long as the PATH does not
  contain a stale `chromedriver.exe` from an older version.
- **App must be running:** `run_e2e.ps1` does **not** start the backend. The app must be started
  first via `run_app.ps1` with `ASPNETCORE_ENVIRONMENT=Development` so that the
  `/api/test/reset` and `/api/test/seed` endpoints are available.
- **Date input format:** `<input type="date">` values are set via JavaScript executor rather than
  `SendKeys`, because Chrome date inputs have separate month/day/year segments that make raw
  keystroke injection locale-dependent and unreliable.
- **Alert-based errors:** The frontend surfaces server errors (e.g. duplicate title, bad request)
  via `alert()`. E2E tests assert on the alert text and dismiss it, matching the current UI
  behaviour rather than assuming a dedicated error element.

## Test data strategy

- Every scenario that reads or mutates state starts with `Given I reset data` (calls
  `DELETE /api/test/reset`) so each test is fully isolated and order-independent.
- Fixtures that need specific items call `And I seed todos:` (calls `POST /api/test/seed`)
  with a Gherkin table, keeping the data declaration co-located with the scenario it supports.
- Service tests create a fresh `InMemoryTodoRepository` per test via the `NewSvc()` helper —
  no shared state, no cleanup needed.
- Relative date offsets (`+Nd` / `-Nd`) are used in both feature tables and step definitions
  so tests remain correct regardless of when they are run.

## Split between E2E and service tests

| Concern | E2E | Service |
|---|---|---|
| UI rendering (labels, badges, chips) | ✅ | — |
| Routing / HTTP status codes | ✅ (via UI interactions) | — |
| Business logic (validation, dedup, tag sanitisation) | representative scenarios | exhaustive edge cases |
| Filter / sort correctness | representative scenarios | exhaustive parameter combinations |
| Bulk operation counts | happy path | mixed success/failure counts |
| Idempotency (complete/uncomplete) | — | ✅ |
| Locked-item delete | — | ✅ |

## Improvements I would make next

1. **Headless mode flag** — add a `--headless` switch to `run_e2e.ps1` / the Chrome options so
   the suite can run in CI without a display.
2. **Retry on stale element in `FindRow`** — `FindRow` walks `<li>` elements and calls
   `.FindElement` on each; if the list re-renders mid-loop the child lookup throws. Wrapping
   the inner `FindElement` call in a `try/catch (StaleElementReferenceException)` and retrying
   would make it more robust.
3. **Page Object Model** — extract the repeated selector constants and action helpers into a
   `TodosPage` page-object class to reduce duplication between `TodosSteps` and
   `AdvancedTodosSteps` and make future selector changes a single-point edit.
4. **Search scenario** — add a Gherkin scenario covering the search-by-title and
   search-by-tag flows (both are exercised in service tests but have no E2E coverage).
5. **Due-date filter scenario** — `dueBefore`/`dueAfter` query parameters are tested at the
   service layer but not via the UI (the UI does not expose those filters directly).
6. **Locked-item E2E scenario** — the `Locked` flag is covered in service tests; an E2E
   scenario could verify the UI surfaces the 409 Conflict error when a user tries to delete a
   locked item.
7. **CI pipeline** — add a GitHub Actions workflow that installs .NET 10, starts the app in the
   background, runs both test suites, and publishes a test-results artifact.
