using System.Text.Json.Nodes;
using Middt.Framework.Expressions;

namespace Middt.Framework.Workflow.Expressions;

/// <summary>
/// Evaluates C#-like expressions used in rules, mappings and task templates over the workflow variables
/// (<c>data</c>, <c>input</c>, <c>result</c>, <c>child</c>, <c>user</c>, <c>instance</c>, <c>now</c>).
/// Thin wrapper over the shared <see cref="ExpressionEvaluator"/>.
/// </summary>
public sealed class WorkflowExpressionEngine
{
    private readonly ExpressionEvaluator evaluator = new(["data", "input", "child", "instance"], ["result"]);

    /// <summary>Compiles <paramref name="expression"/> and returns the error message, or <c>null</c> when it is valid.</summary>
    public string? Validate(string expression) => evaluator.Validate(expression);

    /// <summary>Validates every <c>{{ expression }}</c> placeholder of a template value.</summary>
    public IEnumerable<string> ValidateTemplate(JsonNode? template) => evaluator.ValidateTemplate(template);

    public object? Evaluate(string expression, ExpressionScope scope) => evaluator.Evaluate(expression, ToContext(scope));

    public bool EvaluateCondition(string expression, ExpressionScope scope) => evaluator.EvaluateCondition(expression, ToContext(scope));

    public JsonNode? EvaluateToNode(string expression, ExpressionScope scope) => evaluator.EvaluateToNode(expression, ToContext(scope));

    /// <inheritdoc cref="ExpressionEvaluator.Render"/>
    public JsonNode? Render(JsonNode? template, ExpressionScope scope) => evaluator.Render(template, ToContext(scope));

    public string RenderText(string text, ExpressionScope scope) => evaluator.RenderText(text, ToContext(scope));

    private static ExpressionContext ToContext(ExpressionScope scope) =>
        new ExpressionContext { User = scope.User, Now = scope.Now }
            .Set("data", scope.Data)
            .Set("input", scope.Input)
            .Set("result", scope.Result)
            .Set("child", scope.Child)
            .Set("instance", scope.Instance);
}
