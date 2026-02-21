using Endfield.BlcTool.Core.Models;

namespace Endfield.BlcTool.Core.Blc;

/// <summary>
/// High-level decode pipeline for Endfield .blc metadata:
/// decrypt first, then parse into strongly typed model.
/// </summary>
public static class BlcDecoder
{
    /// <summary>
    /// Decodes encrypted .blc bytes into <see cref="BlcMainInfo"/>.
    /// </summary>
    public static BlcMainInfo Decode(byte[] encryptedBlc)
    {
        var decrypted = BlcDecryptor.Decrypt(encryptedBlc);
        return BlcParser.Parse(decrypted);
    }
}
