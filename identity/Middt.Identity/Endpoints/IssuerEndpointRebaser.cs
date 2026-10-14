using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Middt.Identity.Endpoints;

/// <summary>
/// Advertises the discovery endpoints under the configured issuer instead of the request's host. Relying parties fetch
/// discovery over an internal address (e.g. <c>http://middt-identity:8080/</c> inside a container network) but
/// browsers must be redirected to the public one; OpenIddict derives endpoint URIs from the request, so without this an
/// internal host name would leak into the authorization redirect.
/// </summary>
internal sealed class IssuerEndpointRebaser : IOpenIddictServerHandler<HandleConfigurationRequestContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
        OpenIddictServerHandlerDescriptor.CreateBuilder<HandleConfigurationRequestContext>()
            .UseSingletonHandler<IssuerEndpointRebaser>()
            .SetOrder(OpenIddictServerHandlers.Discovery.AttachEndpoints.Descriptor.Order + 1)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public ValueTask HandleAsync(HandleConfigurationRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Options.Issuer is not { } issuer || context.BaseUri is not { } requestBase)
        {
            return ValueTask.CompletedTask;
        }

        var publicBase = issuer.AbsoluteUri.EndsWith('/') ? issuer : new Uri(issuer.AbsoluteUri + "/");
        context.AuthorizationEndpoint = Rebase(context.AuthorizationEndpoint, requestBase, publicBase);
        context.DeviceAuthorizationEndpoint = Rebase(context.DeviceAuthorizationEndpoint, requestBase, publicBase);
        context.EndSessionEndpoint = Rebase(context.EndSessionEndpoint, requestBase, publicBase);
        context.IntrospectionEndpoint = Rebase(context.IntrospectionEndpoint, requestBase, publicBase);
        context.JsonWebKeySetEndpoint = Rebase(context.JsonWebKeySetEndpoint, requestBase, publicBase);
        context.PushedAuthorizationEndpoint = Rebase(context.PushedAuthorizationEndpoint, requestBase, publicBase);
        context.RevocationEndpoint = Rebase(context.RevocationEndpoint, requestBase, publicBase);
        context.TokenEndpoint = Rebase(context.TokenEndpoint, requestBase, publicBase);
        context.UserInfoEndpoint = Rebase(context.UserInfoEndpoint, requestBase, publicBase);
        return ValueTask.CompletedTask;
    }

    /// <summary>Moves <paramref name="endpoint"/> from the request base to the public base; other URIs are kept.</summary>
    internal static Uri? Rebase(Uri? endpoint, Uri requestBase, Uri publicBase)
    {
        if (endpoint is null || !endpoint.IsAbsoluteUri)
        {
            return endpoint;
        }

        var normalizedBase = requestBase.AbsoluteUri.EndsWith('/') ? requestBase : new Uri(requestBase.AbsoluteUri + "/");
        return normalizedBase.IsBaseOf(endpoint)
            ? new Uri(publicBase, normalizedBase.MakeRelativeUri(endpoint))
            : endpoint;
    }
}
