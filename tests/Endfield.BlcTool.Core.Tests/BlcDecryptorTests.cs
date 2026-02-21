using Endfield.BlcTool.Core.Blc;
using Xunit;

namespace Endfield.BlcTool.Core.Tests;

public class BlcDecryptorTests
{
    [Fact]
    public void Decrypt_Throws_WhenInputTooShort()
    {
        Assert.Throws<ArgumentException>(() => BlcDecryptor.Decrypt(new byte[8]));
    }
}
