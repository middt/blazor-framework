using Middt.Framework.Common.Model.Data;
using Middt.Framework.Client.Security;
using Microsoft.Extensions.Options;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Model.Authentication;
using Middt.Framework.Tests.Api;

namespace Middt.Framework.Tests.Client;

/// <summary>Typed clients against the real in-process API (wire compatibility of routes and envelopes).</summary>
public sealed class EndToEndClientTests(DevelopmentApiFixture fixture) : IClassFixture<DevelopmentApiFixture>
{
    [Fact]
    public async Task Typed_clients_login_and_call_secured_crud_endpoints()
    {
        var ct = TestContext.Current.CancellationToken;
        var tokenService = new BaseTokenService(
            fixture.Host.Client,
            Options.Create(new ApiSettings { URL = "http://localhost", Version = "1.0" }),
            NullLoggers.For<BaseTokenService>());

        var login = await tokenService.LoginAsync(new LoginRequestModel { username = FakeUserStore.Username, password = FakeUserStore.Password }, ct);
        login.IsSuccess.Should().BeTrue();

        var session = new FakeSessionState();
        await session.SetToken(login);
        var client = new ItemsApiClient(fixture.Host.Client, session, tokenService, controllerName: "secureitems");

        var inserted = await client.InsertAsync(new ApiItem { Name = "E2E", Quantity = 4 }, ct);
        inserted.IsSuccess.Should().BeTrue(inserted.ErrorText);

        var search = await client.GetAllAsync(ct);
        search.IsSuccess.Should().BeTrue();
        search.Data.Should().ContainSingle(i => i.Id == inserted.Data!.Id);

        var invalid = await client.InsertAsync(new ApiItem { Name = "" }, ct);
        invalid.IsSuccess.Should().BeFalse();
        invalid.MessageList.Should().NotBeEmpty();

        var anonymous = new ItemsApiClient(fixture.Host.Client, new FakeSessionState(), tokenService, controllerName: "secureitems");
        (await anonymous.GetAllAsync(ct)).IsSuccess.Should().BeFalse();
    }
}
