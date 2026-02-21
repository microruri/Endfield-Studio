using Endfield.BlcTool.Core.Models;

namespace Endfield.BlcTool.Core.Blc;

public static class BlcDecoder
{
    public static BlcMainInfo Decode(byte[] encryptedBlc)
    {
        var decrypted = BlcDecryptor.Decrypt(encryptedBlc);
        return BlcParser.Parse(decrypted);
    }
}
