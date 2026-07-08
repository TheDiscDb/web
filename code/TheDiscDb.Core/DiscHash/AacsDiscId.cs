using System.Security.Cryptography;

namespace TheDiscDb.Core.DiscHash;

/// <summary>
/// Computes the AACS Disc ID: <c>uppercase hex( SHA1( AACS/Unit_Key_RO.inf bytes ) )</c>.
/// <para>
/// The Disc ID is a globally-stable, per-pressing identifier used by libbluray, libaacs,
/// MakeMKV, and keydb.cfg (e.g. <c>4D60474073344DD856B68E2195359E8F3E9DAD21</c>).
/// It is <em>distinct from the decryption key (VUK)</em> — computing it requires
/// <em>no AACS keys</em>, only the unencrypted <c>Unit_Key_RO.inf</c> bytes.
/// </para>
/// <para>
/// Valid for Blu-ray and UHD/4K discs. DVDs use a different algorithm — see
/// <see cref="DvdDiscId"/>.
/// </para>
/// </summary>
public static class AacsDiscId
{
    /// <summary>
    /// Computes the AACS Disc ID from the raw bytes of <c>AACS/Unit_Key_RO.inf</c>.
    /// Returns a 40-character uppercase hex string with no prefix or separators.
    /// </summary>
    public static string Compute(byte[] unitKeyRoInfBytes)
    {
        ArgumentNullException.ThrowIfNull(unitKeyRoInfBytes);
        return Convert.ToHexString(SHA1.HashData(unitKeyRoInfBytes));
    }

    /// <summary>
    /// Computes the AACS Disc ID from a stream containing the bytes of
    /// <c>AACS/Unit_Key_RO.inf</c>.
    /// Returns a 40-character uppercase hex string with no prefix or separators.
    /// </summary>
    public static string Compute(Stream unitKeyRoInfStream)
    {
        ArgumentNullException.ThrowIfNull(unitKeyRoInfStream);
        return Convert.ToHexString(SHA1.HashData(unitKeyRoInfStream));
    }
}
