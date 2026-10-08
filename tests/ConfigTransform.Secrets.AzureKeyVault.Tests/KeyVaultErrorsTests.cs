using System.Text;
using Azure;
using Azure.Core;
using Azure.Identity;
using Xunit;

namespace ConfigTransform.Secrets.AzureKeyVault.Tests;

/// <summary>
/// How each Azure failure is worded in the report (docs/KEYVAULT_SECRETS_DESIGN.md, "What the report
/// shows") — and that nothing of the original exception's message ever comes through, since it can
/// carry a whole response body. Every message below contains <see cref="Leak"/>; no description may.
/// </summary>
public class KeyVaultErrorsTests
{
    private const string Leak = "LEAKED-RESPONSE-BODY-1234";

    public static TheoryData<Exception, string> Failures => new()
    {
        { new CredentialUnavailableException($"Azure CLI not installed {Leak}"), "can't be read (not signed in to Azure -- run az login)" },
        { new AuthenticationFailedException($"AADSTS700082: refresh token expired {Leak}"), "can't be read (Azure sign-in failed -- run az login)" },
        { new RequestFailedException(0, $"No such host is known. (kv-x.vault.azure.net:443) {Leak}"), "can't be reached (no such vault, or no network)" },
        { new AggregateException(new RequestFailedException(0, Leak), new RequestFailedException(0, Leak)), "can't be reached (no such vault, or no network)" },
        { new HttpRequestException(Leak), "can't be reached (no such vault, or no network)" },
        { Forbidden("ForbiddenByRbac"), "can't be read (403 ForbiddenByRbac: no access)" },
        { Forbidden("ForbiddenByFirewall"), "can't be read (403 ForbiddenByFirewall: blocked by the vault's network rules)" },
        { Forbidden("ForbiddenByPolicy"), "can't be read (403 ForbiddenByPolicy: no access policy for this caller)" },
        { Forbidden("Something\nInjected"), "can't be read (403 Forbidden: forbidden)" }, // an odd code falls back to error.code
        { new RequestFailedException(new FakeResponse(429, $$"""{ "error": { "code": "Throttled", "message": "{{Leak}}" } }""")), "can't be read (429 Throttled)" },
        { new InvalidCastException(Leak), "can't be read (InvalidCastException)" },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public void Each_failure_gets_its_own_wording_and_none_of_its_message(Exception failure, string expected)
    {
        var description = KeyVaultErrors.Describe(failure);

        Assert.Equal(expected, description);
        Assert.DoesNotContain(Leak, description);
    }

    [Fact]
    public void A_401_names_the_tenant_the_vaults_challenge_points_at()
    {
        var response = new FakeResponse(401, $$"""{ "error": { "code": "Unauthorized", "message": "{{Leak}}" } }""",
            ("WWW-Authenticate", "Bearer authorization=\"https://login.microsoftonline.com/72f988bf-86f1-41af-91ab-2d7cd011db47\", resource=\"https://vault.azure.net\""));

        Assert.Equal(
            "can't be read (it's in another tenant -- run az login --tenant 72f988bf-86f1-41af-91ab-2d7cd011db47)",
            KeyVaultErrors.Describe(new RequestFailedException(response)));
    }

    [Fact]
    public void A_401_without_a_challenge_still_points_at_the_tenant()
    {
        Assert.Equal(
            "can't be read (401: not authorized -- check the tenant you're signed in to)",
            KeyVaultErrors.Describe(new RequestFailedException(new FakeResponse(401, null))));
    }

    private static RequestFailedException Forbidden(string innerCode) =>
        new(new FakeResponse(403, $$"""
            { "error": { "code": "Forbidden", "message": "Caller is not authorized. {{Leak}}", "innererror": { "code": "{{innerCode.Replace("\n", "\\n")}}" } } }
            """));

    /// <summary>The smallest <see cref="Response"/> a <see cref="RequestFailedException"/> can be built from.</summary>
    private sealed class FakeResponse : Response
    {
        private readonly int _status;
        private readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);

        public FakeResponse(int status, string? content, params (string Name, string Value)[] headers)
        {
            _status = status;
            ContentStream = content is null ? null : new MemoryStream(Encoding.UTF8.GetBytes(content));
            foreach (var (name, value) in headers)
                _headers[name] = value;
        }

        public override int Status => _status;
        public override string ReasonPhrase => "";
        public override Stream? ContentStream { get; set; }
        public override string ClientRequestId { get; set; } = "";

        public override void Dispose()
        {
        }

        protected override bool TryGetHeader(string name, out string value)
        {
            var found = _headers.TryGetValue(name, out var v);
            value = v!;
            return found;
        }

        protected override bool TryGetHeaderValues(string name, out IEnumerable<string> values)
        {
            var found = _headers.TryGetValue(name, out var v);
            values = found ? [v!] : [];
            return found;
        }

        protected override bool ContainsHeader(string name) => _headers.ContainsKey(name);

        protected override IEnumerable<HttpHeader> EnumerateHeaders() => _headers.Select(h => new HttpHeader(h.Key, h.Value));
    }
}
