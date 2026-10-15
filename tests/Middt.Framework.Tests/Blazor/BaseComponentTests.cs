using Middt.Framework.Blazor.Web.Base;

namespace Middt.Framework.Tests.Blazor;

public sealed class BaseComponentTests
{
    [Fact]
    public void Component_cancellation_is_cancelled_and_still_readable_after_dispose()
    {
        // Loads that finish after the user navigated away read the token of the disposed component.
        var component = new TestComponent();
        var before = component.Token;

        component.Dispose();

        before.IsCancellationRequested.Should().BeTrue();
        var after = () => component.Token;
        after.Should().NotThrow();
        component.Token.IsCancellationRequested.Should().BeTrue();
    }

    private sealed class TestComponent : BaseComponent
    {
        public CancellationToken Token => ComponentCancellation;
    }
}
