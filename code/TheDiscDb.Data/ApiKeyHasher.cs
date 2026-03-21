using System;
using System.Security.Cryptography;
using System.Text;

namespace TheDiscDb.Web.Data;

public static class ApiKeyHasher
{
    public static string HashKey(string apiKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToBase64String(bytes);
    }
}
