using SchemaDoc.Extraction.Extractors;

namespace SchemaDoc.Extraction.Tests;

/// <summary>
/// Confirms that connection failures surface a meaningful exception
/// rather than silently returning false (which used to make MFA/Azure AD
/// issues impossible to diagnose).
/// </summary>
public class ConnectionErrorReportingTests
{
    [Fact]
    public async Task Bad_server_throws_with_readable_message()
    {
        var extractor = new SqlServerExtractor();
        var cs = "Server=does-not-exist-xyz.database.windows.net;Database=none;User Id=x;Password=y;Connect Timeout=5;";

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => extractor.TestConnectionAsync(cs));

        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
        // The message should mention the network or DNS failure — not just be "False"
        Assert.DoesNotContain("False", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Interactive_auth_connection_string_shape_is_valid()
    {
        // We can't actually run the interactive flow in a test, but we can verify
        // the connection string is accepted by the SqlClient parser (no ArgumentException).
        var extractor = new SqlServerExtractor();
        var cs = "Server=test.database.windows.net;Database=mydb;Authentication=Active Directory Interactive;User Id=me@company.com;Connect Timeout=5;";

        // Will throw because the server doesn't exist / auth won't complete, but should
        // throw a "network" / "MSAL" / "timeout" error — never an "unknown keyword" error.
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => extractor.TestConnectionAsync(cs));
        Assert.DoesNotContain("Keyword not supported", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
