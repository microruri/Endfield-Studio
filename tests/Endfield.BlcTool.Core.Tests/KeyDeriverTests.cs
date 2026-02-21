using Endfield.BlcTool.Core.Crypto;
using Xunit;

namespace Endfield.BlcTool.Core.Tests;

public class KeyDeriverTests
{
    [Fact]
    public void GetCommonChachaKey_ReturnsExpectedHex()
    {
        var key = KeyDeriver.GetCommonChachaKey();

        Assert.Equal(32, key.Length);
        Assert.Equal("E95B317AC4F828569D23A86BF271DCB53E846FA75C924D671DBA8E38F4CA52E1", Convert.ToHexString(key));
    }
}
