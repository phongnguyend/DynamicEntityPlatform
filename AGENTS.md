# Coding conventions

- Always use braces for control-flow bodies, even when the body contains only one statement. This applies to `if`, `else if`, `else`, `for`, `foreach`, `while`, `do`, and similar constructs in languages that support braces.
- Put the body on separate lines rather than writing a single-line control-flow statement. Follow the surrounding language's brace placement style.
- Never compress a braced block onto one line, such as `if (condition) { DoSomething(); return; }`. Put each statement on its own line. In C#, put opening and closing braces on separate lines as well. Preferred C# formatting:

```csharp
if (context.User.Identity?.IsAuthenticated != true)
{
    context.Response.StatusCode = 401;
    return;
}
```

- In C#, separate each property declaration from the next with one blank line, including auto-properties.
- Apply convention to new and modified code, including tests.

## Button icons

- Every button must display a meaningful icon, including save/cancel actions, mode selectors, and buttons inside dialogs. Use the existing `lucide-react` icons and shared button sizing/spacing.
- Keep icons visible in every state, including loading, disabled, selected, and unselected states. Do not add a second icon to buttons that already have one.
- Keep descriptive text labels for text buttons. Mark decorative icons with `aria-hidden="true"`; icon-only buttons must have a descriptive `aria-label` and a tooltip (`title`).
- Apply the same convention to links styled as action buttons.
- Verify icon changes with `frontend/e2e/button-icons.spec.ts`. Reuse `expectButtonIcons` from `frontend/e2e/button-icon-assertions.ts` when adding pages, dialogs, or button states; add targeted checks for the intended symbol when icon choice matters.

## Frontend regression checks

- Prefer the repeatable Playwright scripts in `frontend/e2e` over ad hoc interactive browser sessions for UI verification.
- Run the relevant feature spec with `npm run test:e2e -- e2e/<feature>.spec.ts` from `frontend`. Add or update regression cases when changing user-visible behavior.
- Use interactive browser inspection when diagnosing failures or assessing visual design; preserve reproducible checks in the test suite.
- The browser suite uses isolated API fixtures. Keep backend behavior covered by the .NET unit/integration tests; do not describe mocked UI tests as database integration coverage.
