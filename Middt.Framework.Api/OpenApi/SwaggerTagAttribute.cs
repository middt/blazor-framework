namespace Middt.Framework.Api.Swagger;

/// <summary>
/// Marks an action as part of the public API documentation outside Development
/// (see <see cref="MiddtApiOptions.HideUntaggedOperationsOutsideDevelopment"/>).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class SwaggerTagAttribute(bool shownInRelease) : Attribute
{
    public bool ShownInRelease { get; } = shownInRelease;
}
