# ValIO — engineering conventions for Codex

## Code comments & XML documentation (C#)

These rules are **mandatory** for any C# code you write or edit in this repository.

- **Language: English.** Write every code comment and XML documentation comment in English, even where surrounding code currently uses another language.
- **Document everything.** Add C# XML documentation comments to **every** type and member — classes, records, structs, interfaces, enums (and enum members), methods, constructors, properties, indexers, events, and fields — **regardless of accessibility** (`public`, `internal`, `protected`, `private`) and including `static` members and local/nested types.
- **Use the full XML tag vocabulary** where it adds value:
  - `<summary>` — always; describe *what* the member does and *why*, not a restatement of its signature.
  - `<remarks>` — design rationale, invariants, edge cases, threading/ordering assumptions.
  - `<param>`, `<typeparam>`, `<returns>`, `<value>` — for every parameter, type parameter, return value, and property value.
  - `<exception cref="..."/>` — for each exception the member can throw, with the condition.
  - `<see cref="..."/>` and `<seealso cref="..."/>` — cross-link related types/members so the docs are navigable.
  - `<example>` + `<code>` — for non-obvious usage.
  - `<inheritdoc/>` — when overriding/implementing, to avoid duplicating base docs.
  - `<paramref name="..."/>` / `<typeparamref name="..."/>` inside prose.
- **When touching an undocumented member, add the docs** as part of the change.
- Keep `cref` references valid (they must resolve) so the documentation stays compiler-checked.
