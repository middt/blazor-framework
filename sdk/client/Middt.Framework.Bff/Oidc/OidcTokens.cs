using System.Text.Json;
using Middt.Framework.Model.Authentication;

namespace Middt.Framework.Bff.Oidc;

/// <summary>
/// The identity token of an OIDC session travels in <see cref="TokenResponseModel.listTokenResponseAdditionalDataModel"/>
/// (key <see cref="IdTokenKey"/>) of the server side token store entry; it is only used as <c>id_token_hint</c> at logout.
/// </summary>
public static class OidcTokens
{
    public const string IdTokenKey = "id_token";

    /// <summary>The stored identity token, or <c>null</c>.</summary>
    public static string? GetIdToken(TokenResponseModel? tokens) =>
        tokens?.listTokenResponseAdditionalDataModel?.FirstOrDefault(d => d.Key == IdTokenKey)?.Value switch
        {
            string value => value,
            // Entries read back from the store are deserialized as JsonElement.
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => null,
        };

    /// <summary>Replaces the identity token of <paramref name="tokens"/>.</summary>
    public static void SetIdToken(TokenResponseModel tokens, string? idToken)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        var list = tokens.listTokenResponseAdditionalDataModel?.Where(d => d.Key != IdTokenKey).ToList() ?? [];
        if (!string.IsNullOrEmpty(idToken))
        {
            list.Add(new TokenResponseAdditionalDataModel { Key = IdTokenKey, Value = idToken });
        }

        tokens.listTokenResponseAdditionalDataModel = list.Count > 0 ? list : null;
    }
}
