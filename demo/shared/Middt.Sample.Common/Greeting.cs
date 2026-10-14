namespace Middt.Sample.Common;

/// <summary>Sample service registered from configuration (see <c>DependencySettings</c> in appsettings.json).</summary>
public interface IGreeting
{
    string DoSomething(string parameter);
}

public sealed class Greeting : IGreeting
{
    public string DoSomething(string parameter) => $"Message from Greeting with {parameter}";
}
