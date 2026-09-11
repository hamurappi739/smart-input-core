using System.Security.Cryptography;
using System.Text;

namespace SmartInput.Core.Tests;

internal static class AuditCaseHasher
{
    internal static string HashCase(string mutation, string source, string? replacement)
    {
        var material = $"{mutation}\u001f{source}\u001f{replacement ?? string.Empty}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(hash)[..16];
    }
}
