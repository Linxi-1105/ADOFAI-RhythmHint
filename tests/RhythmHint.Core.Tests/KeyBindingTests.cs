using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

/// <summary>设置里的键位文字：拆分、自定义某一个键、按键框里显示的字。</summary>
public class KeyBindingTests
{
    [Fact]
    public void Split_AlwaysGivesFourKeys()
    {
        Assert.Equal(new[] { "D", "F", "J", "K" }, KeyBindings.Split("D F J K"));
        Assert.Equal(new[] { "D", "F", "J", "K" }, KeyBindings.Split("  D  F J K  "));
        Assert.Equal(new[] { "D", "F", "J", KeyBindings.Unbound }, KeyBindings.Split("D F J")); // 手改设置文件少写了一个
        Assert.Equal(new[] { "A", "S", "D", "F" }, KeyBindings.Split("A S D F G"));
        Assert.All(KeyBindings.Split(null), k => Assert.Equal(KeyBindings.Unbound, k));
    }

    [Fact]
    public void Assign_ReplacesOneKey()
    {
        Assert.Equal("D F J ;", KeyBindings.Assign("D F J K", 3, ";"));
        Assert.Equal("LeftShift F J K", KeyBindings.Assign("D F J K", 0, "LeftShift"));
    }

    [Fact]
    public void Assign_SwapsWhenTheKeyIsAlreadyUsed()
    {
        // 第 1 个键改成 J，原来第 3 个就是 J：两个位置互换，不会出现两个 J
        Assert.Equal("J F D K", KeyBindings.Assign("D F J K", 0, "J"));
        Assert.Equal("J F D K", KeyBindings.Assign("d F J K", 2, "D")); // 手改设置文件写了小写 d，也认得出是同一个键
        Assert.Equal("D F J K", KeyBindings.Assign("D F J K", 2, "J")); // 选的就是它自己：不变
    }

    [Fact]
    public void Assign_KeepsPositionsWhenSomeKeysAreUnbound()
    {
        // 空位写成 None 占住位置，换来换去其他键不会挪位
        var labels = KeyBindings.Assign("D F", 2, "D");
        Assert.Equal("None F D None", labels);
        Assert.Equal(new[] { KeyBindings.Unbound, "F", "D", KeyBindings.Unbound }, KeyBindings.Split(labels));
    }

    [Fact]
    public void Assign_RejectsBadInput()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => KeyBindings.Assign("D F J K", 4, "A"));
        Assert.Throws<ArgumentException>(() => KeyBindings.Assign("D F J K", 0, ""));
        Assert.Throws<ArgumentException>(() => KeyBindings.Assign("D F J K", 0, "A B"));
    }

    [Theory]
    [InlineData("D", "D")]
    [InlineData("d", "D")]
    [InlineData(";", ";")]
    [InlineData("LeftShift", "LShift")]
    [InlineData("RightControl", "RCtrl")]
    [InlineData("Return", "Enter")]
    [InlineData("UpArrow", "↑")]
    [InlineData("Space", "Space")]
    [InlineData("None", "")]
    [InlineData("Keypad4", "Keypad4")] // 屏幕键盘里没有的键（手改设置文件）原样显示
    public void Display_ShortensLongNames(string key, string shown) => Assert.Equal(shown, KeyBindings.Display(key));
}
