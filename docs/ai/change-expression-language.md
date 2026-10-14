# Playbook: change the portable expression language

The language is a cross-platform contract: .NET (`sdk/shared/Middt.Framework.Expressions/Portable`) and TypeScript
(`sdk/client/typescript/ui-core/src/expression.ts`) must behave **identically**.

1. Write the intended behaviour as cases in `spec/expressions.conformance.json` (`expr`/`template`/`truthy` +
   `expected`, or `error: true`). Cover edge cases: null propagation, types, number text (ECMAScript rules), errors.
2. Run both suites and watch the new cases fail:
   `dotnet test --project tests/Middt.Framework.Tests -- --filter-class "*PortableExpressionConformanceTests"` and
   `(cd sdk/client/typescript/ui-core && node --test test/)`.
3. Implement in C# (lexer/parser/evaluator) and TypeScript. New methods: add to the whitelist in both parsers.
4. Both suites green; update the language summary in the XML doc of `PortableExpression` and the README section.
5. Rebuild ui-core and the Angular library before testing the app.

Do not: add reflection-like features, allow arbitrary method calls, or make evaluation throw on data errors
(evaluation must be total – undefined → null).
