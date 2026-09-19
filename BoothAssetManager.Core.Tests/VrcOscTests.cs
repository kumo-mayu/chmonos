using System.Text;
using BoothAssetManager.Core.Services;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>VRChat の OSC へ送る1通の形（OSC 1.0：各部分を 0 で終え、4バイト区切りに揃える）。</summary>
public sealed class VrcOscTests
{
    [Fact]
    public void 各部分を4バイト区切りに揃える()
    {
        var message = VrcOsc.StringMessage("/avatar/change", "avtr_x");

        // "/avatar/change" は14バイト → 0 を足して16。",s" は2 → 4。"avtr_x" は6 → 8
        Assert.Equal(16 + 4 + 8, message.Length);
        Assert.Equal("/avatar/change", Encoding.UTF8.GetString(message, 0, 14));
        Assert.Equal(new byte[] { 0, 0 }, message[14..16]);
        Assert.Equal(",s", Encoding.UTF8.GetString(message, 16, 2));
        Assert.Equal("avtr_x", Encoding.UTF8.GetString(message, 20, 6));
        Assert.Equal(new byte[] { 0, 0 }, message[26..28]);
    }

    [Fact]
    public void 長さがちょうど4の倍数でも終わりの0を1区切り足す()
    {
        // "abc" + 0 で4、"abcd" は 0 を入れる余地が無いので8まで伸ばす
        var message = VrcOsc.StringMessage("/abc", "abcd");

        Assert.Equal(8 + 4 + 8, message.Length);
        Assert.Equal(0, message[4]);
        Assert.Equal(0, message[16]);
    }

    [Theory]
    [InlineData("avtr_0123abcd-4567-89ab-cdef-0123456789ab", true)]
    [InlineData("  avtr_0123ABCD-4567-89AB-CDEF-0123456789AB  ", true)]
    [InlineData("usr_0123abcd-4567-89ab-cdef-0123456789ab", false)]
    [InlineData("avtr_0123abcd", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void blueprintIDの形を見分ける(string? text, bool expected)
        => Assert.Equal(expected, VrcOsc.LooksLikeAvatarId(text));
}
