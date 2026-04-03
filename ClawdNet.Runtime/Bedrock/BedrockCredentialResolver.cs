using System.Security.Cryptography;
using System.Text;

namespace ClawdNet.Runtime.Bedrock;

/// <summary>
/// Resolves AWS credentials and region configuration for Bedrock API access.
/// Supports standard AWS credentials, bearer token auth, and skip-auth mode.
/// </summary>
public sealed class BedrockCredentialResolver
{
    private readonly string? _accessKeyId;
    private readonly string? _secretAccessKey;
    private readonly string? _sessionToken;
    private readonly string? _bearerToken;
    private readonly bool _skipAuth;
    public string Region { get; }
    public string? CustomEndpoint { get; }
    public string? SessionToken => _sessionToken;

    public BedrockCredentialResolver()
        : this(
            accessKeyId: Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID"),
            secretAccessKey: Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY"),
            sessionToken: Environment.GetEnvironmentVariable("AWS_SESSION_TOKEN"),
            bearerToken: Environment.GetEnvironmentVariable("AWS_BEARER_TOKEN_BEDROCK"),
            skipAuth: Environment.GetEnvironmentVariable("CLAUDE_CODE_SKIP_BEDROCK_AUTH") is "1" or "true",
            region: Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION")
                  ?? Environment.GetEnvironmentVariable("AWS_REGION"),
            customEndpoint: Environment.GetEnvironmentVariable("ANTHROPIC_BEDROCK_BASE_URL"))
    {
    }

    public BedrockCredentialResolver(
        string? accessKeyId,
        string? secretAccessKey,
        string? sessionToken,
        string? bearerToken,
        bool skipAuth,
        string? region,
        string? customEndpoint)
    {
        _accessKeyId = accessKeyId;
        _secretAccessKey = secretAccessKey;
        _sessionToken = sessionToken;
        _bearerToken = bearerToken;
        _skipAuth = skipAuth;
        Region = string.IsNullOrWhiteSpace(region) ? "us-east-1" : region.Trim();
        CustomEndpoint = string.IsNullOrWhiteSpace(customEndpoint) ? null : customEndpoint.Trim();
    }

    public bool HasCredentials => _skipAuth || !string.IsNullOrWhiteSpace(_bearerToken) ||
        (!string.IsNullOrWhiteSpace(_accessKeyId) && !string.IsNullOrWhiteSpace(_secretAccessKey));

    public string? GetBearerToken()
    {
        if (!string.IsNullOrWhiteSpace(_bearerToken))
        {
            return _bearerToken;
        }

        return null;
    }

    public bool UseBearerAuth => !string.IsNullOrWhiteSpace(_bearerToken);
    public bool UseSkipAuth => _skipAuth;
    public bool UseStandardAuth => !UseBearerAuth && !UseSkipAuth &&
        !string.IsNullOrWhiteSpace(_accessKeyId) && !string.IsNullOrWhiteSpace(_secretAccessKey);

    /// <summary>
    /// Builds the Bedrock API endpoint for the given model ID.
    /// Handles ARN-format model IDs and cross-region inference profile prefixes.
    /// </summary>
    public string BuildEndpoint(string modelId)
    {
        if (!string.IsNullOrWhiteSpace(CustomEndpoint))
        {
            var baseEndpoint = CustomEndpoint.TrimEnd('/');
            return $"{baseEndpoint}/model/{modelId}/converse";
        }

        // Check if modelId is an ARN
        if (modelId.StartsWith("arn:"))
        {
            return $"https://bedrock-runtime.{Region}.amazonaws.com{BuildArnPath(modelId)}";
        }

        // Standard model ID path
        return $"https://bedrock-runtime.{Region}.amazonaws.com/model/{modelId}/converse";
    }

    private static string BuildArnPath(string arn)
    {
        // ARN format: arn:partition:service:region:account:resource
        // e.g., arn:aws:bedrock:us-east-1:123456789012:inference-profile/us.anthropic.claude-sonnet-4-5-20250514-v1:0
        // The resource part (6th field) can contain colons (e.g., v1:0), so split on first 5 colons only.
        var colonCount = 0;
        var resourceStart = 0;
        for (var i = 0; i < arn.Length; i++)
        {
            if (arn[i] == ':')
            {
                colonCount++;
                if (colonCount == 5)
                {
                    resourceStart = i + 1;
                    break;
                }
            }
        }

        if (resourceStart > 0 && resourceStart < arn.Length)
        {
            var resource = arn[resourceStart..];
            return $"/{resource}/converse";
        }

        return $"/{arn}/converse";
    }

    /// <summary>
    /// Computes the AWS SigV4 signature for the given request.
    /// Returns the Authorization header value.
    /// </summary>
    public string ComputeSignature(
        string method,
        string uri,
        string payloadHash,
        string requestBody,
        DateTime utcNow)
    {
        if (!UseStandardAuth)
        {
            throw new InvalidOperationException("Standard AWS credentials are not available. Use bearer token or skip-auth mode.");
        }

        const string service = "bedrock";
        var dateStamp = utcNow.ToString("yyyyMMdd");
        var amzDate = utcNow.ToString("yyyyMMdd'T'HHmmss'Z'");
        var credentialScope = $"{dateStamp}/{Region}/{service}/aws4_request";

        // Build canonical request
        var canonicalRequest = new StringBuilder();
        canonicalRequest.AppendLine(method);
        canonicalRequest.AppendLine(uri);
        canonicalRequest.AppendLine(""); // canonical query string (empty)

        // Canonical headers
        var headers = new List<string>
        {
            $"content-type:application/json",
            $"host:bedrock-runtime.{Region}.amazonaws.com",
            $"x-amz-content-sha256:{payloadHash}",
            $"x-amz-date:{amzDate}"
        };
        if (!string.IsNullOrWhiteSpace(_sessionToken))
        {
            headers.Add($"x-amz-security-token:{_sessionToken}");
        }
        headers.Sort(StringComparer.Ordinal);

        var signedHeaders = string.Join(";", headers.Select(h => h.Split(':')[0]));
        foreach (var header in headers)
        {
            canonicalRequest.AppendLine(header);
        }
        canonicalRequest.AppendLine(""); // trailing newline
        canonicalRequest.AppendLine(signedHeaders);
        canonicalRequest.AppendLine(payloadHash);

        // String to sign
        var canonicalRequestHash = Sha256Hash(canonicalRequest.ToString());
        var stringToSign = new StringBuilder();
        stringToSign.AppendLine("AWS4-HMAC-SHA256");
        stringToSign.AppendLine(amzDate);
        stringToSign.AppendLine(credentialScope);
        stringToSign.Append(canonicalRequestHash);

        // Calculate signature
        var signingKey = GetSignatureKey(_secretAccessKey!, dateStamp, Region, service);
        var signature = BitConverter.ToString(ComputeHmacSha256(stringToSign.ToString(), signingKey)).Replace("-", "").ToLowerInvariant();

        // Build Authorization header
        var credential = $"{_accessKeyId}/{credentialScope}";
        var authorizationHeader = $"AWS4-HMAC-SHA256 Credential={credential}, SignedHeaders={signedHeaders}, Signature={signature}";

        return authorizationHeader;
    }

    /// <summary>
    /// Returns the AMZ date header value for the current time.
    /// </summary>
    public static string GetAmzDate(DateTime utcNow) => utcNow.ToString("yyyyMMdd'T'HHmmss'Z'");

    /// <summary>
    /// Returns the SHA-256 hash of the request payload as a lowercase hex string.
    /// </summary>
    public static string ComputePayloadHash(string payload)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }

    private static byte[] SignKey(byte[] key, string data)
    {
        return ComputeHmacSha256(data, key);
    }

    private static byte[] GetSignatureKey(string key, string dateStamp, string regionName, string serviceName)
    {
        var kDate = SignKey(Encoding.UTF8.GetBytes("AWS4" + key), dateStamp);
        var kRegion = SignKey(kDate, regionName);
        var kService = SignKey(kRegion, serviceName);
        return SignKey(kService, "aws4_request");
    }

    private static string Sha256Hash(string data)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(data));
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }

    private static byte[] ComputeHmacSha256(string data, byte[] key)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
    }
}
