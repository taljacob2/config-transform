using System.Text.Json;
using System.Text.RegularExpressions;
using Azure;
using Azure.Identity;

namespace ConfigTransform.Secrets.AzureKeyVault;

/// <summary>
/// Turns an Azure failure into the phrase the report prints after <c>keyvault://…</c>
/// (docs/KEYVAULT_SECRETS_DESIGN.md, "What the report shows"). Built only from the HTTP status, Key
/// Vault's error code and a tenant ID — never from an exception's message, which can carry a whole
/// response body.
/// </summary>
internal static class KeyVaultErrors
{
    private static readonly Regex SafeCode = new("^[A-Za-z0-9]{1,64}$", RegexOptions.Compiled);

    // The tenant a vault's 401 challenge names: authorization="https://login.microsoftonline.com/<tenant>".
    private static readonly Regex ChallengeTenant = new(
        @"login\.(?:microsoftonline\.com|windows\.net)/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})",
        RegexOptions.Compiled);

    public static string Describe(Exception ex) => ex switch
    {
        // Azure.Core gives up after its retries with an AggregateException of each attempt's failure.
        AggregateException aggregate when aggregate.InnerExceptions.Count > 0 => Describe(aggregate.InnerExceptions[^1]),
        CredentialUnavailableException => "can't be read (not signed in to Azure -- run az login)",
        AuthenticationFailedException => "can't be read (Azure sign-in failed -- run az login)",
        RequestFailedException { Status: 0 } => "can't be reached (no such vault, or no network)",
        RequestFailedException { Status: 401 } failed => ChallengeTenantOf(failed) is { } tenant
            ? $"can't be read (it's in another tenant -- run az login --tenant {tenant})"
            : "can't be read (401: not authorized -- check the tenant you're signed in to)",
        RequestFailedException { Status: 403 } failed => Forbidden(CodeOf(failed)),
        RequestFailedException failed => $"can't be read ({failed.Status} {CodeOf(failed)})",
        HttpRequestException or TaskCanceledException => "can't be reached (no such vault, or no network)",
        _ => $"can't be read ({ex.GetType().Name})",
    };

    private static string Forbidden(string code) => code switch
    {
        "ForbiddenByRbac" => "can't be read (403 ForbiddenByRbac: no access)",
        "ForbiddenByFirewall" or "ForbiddenByConnection" => $"can't be read (403 {code}: blocked by the vault's network rules)",
        "ForbiddenByPolicy" => "can't be read (403 ForbiddenByPolicy: no access policy for this caller)",
        _ => $"can't be read (403 {code}: forbidden)",
    };

    /// <summary>Key Vault's most specific error code — <c>error.innererror.code</c> (<c>ForbiddenByRbac</c>) over <c>error.code</c> (<c>Forbidden</c>) — and only if it looks like a code.</summary>
    internal static string CodeOf(RequestFailedException failed) =>
        new[] { InnerCodeOf(failed), failed.ErrorCode }.FirstOrDefault(code => code is not null && SafeCode.IsMatch(code)) ?? "error";

    private static string? InnerCodeOf(RequestFailedException failed)
    {
        try
        {
            var content = failed.GetRawResponse()?.Content;
            if (content is null)
                return null;

            using var json = JsonDocument.Parse(content.ToMemory());
            return json.RootElement.TryGetProperty("error", out var error)
                   && error.TryGetProperty("innererror", out var inner)
                   && inner.TryGetProperty("code", out var code)
                   && code.ValueKind == JsonValueKind.String
                ? code.GetString()
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? ChallengeTenantOf(RequestFailedException failed)
    {
        var response = failed.GetRawResponse();
        if (response is null || !response.Headers.TryGetValue("WWW-Authenticate", out var challenge))
            return null;
        var match = ChallengeTenant.Match(challenge ?? "");
        return match.Success ? match.Groups[1].Value : null;
    }
}
