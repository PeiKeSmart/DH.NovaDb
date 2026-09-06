using System;
using System.Collections.Generic;
using Xunit;
using NewLife.NovaDb.Engine.Flux;

namespace XUnitTest.Engine.Flux;

/// <summary>MessageId 单元测试</summary>
public class MessageIdTests
{
    [Fact(DisplayName = "测试消息 ID 格式化和解析")]
    public void TestFormatAndParse()
    {
        var id = new MessageId(1000, 5);

        Assert.Equal("1000-5", id.ToString());

        var parsed = MessageId.Parse("1000-5");
        Assert.Equal(1000, parsed.Timestamp);
        Assert.Equal(5, parsed.Sequence);
    }

    [Fact(DisplayName = "测试消息 ID 比较")]
    public void TestComparison()
    {
        var id1 = new MessageId(100, 0);
        var id2 = new MessageId(100, 1);
        var id3 = new MessageId(200, 0);

        // 同时间戳，序号不同
        Assert.True(id1.CompareTo(id2) < 0);
        Assert.True(id2.CompareTo(id1) > 0);

        // 不同时间戳
        Assert.True(id1.CompareTo(id3) < 0);
        Assert.True(id3.CompareTo(id1) > 0);

        // 自身相等
        Assert.Equal(0, id1.CompareTo(new MessageId(100, 0)));
    }

    [Fact(DisplayName = "测试消息 ID 相等性")]
    public void TestEquality()
    {
        var id1 = new MessageId(100, 5);
        var id2 = new MessageId(100, 5);
        var id3 = new MessageId(100, 6);

        Assert.True(id1.Equals(id2));
        Assert.False(id1.Equals(id3));
        Assert.Equal(id1.GetHashCode(), id2.GetHashCode());
    }

    [Fact(DisplayName = "测试消息 ID 自增")]
    public void TestAutoIncrement()
    {
        var entries = new List<FluxEntry>
        {
            new() { Timestamp = 100, SequenceId = 0 },
            new() { Timestamp = 100, SequenceId = 1 },
            new() { Timestamp = 100, SequenceId = 2 }
        };

        var id = MessageId.Auto(entries, 100);
        Assert.Equal(100, id.Timestamp);
        Assert.Equal(3, id.Sequence);
    }

    [Fact(DisplayName = "测试消息 ID 自增-无匹配时间戳")]
    public void TestAutoIncrementNoMatch()
    {
        var entries = new List<FluxEntry>
        {
            new() { Timestamp = 100, SequenceId = 0 },
            new() { Timestamp = 100, SequenceId = 1 }
        };

        var id = MessageId.Auto(entries, 200);
        Assert.Equal(200, id.Timestamp);
        Assert.Equal(0, id.Sequence);
    }

    [Fact(DisplayName = "测试消息 ID 自增-空列表")]
    public void TestAutoIncrementEmpty()
    {
        var entries = new List<FluxEntry>();
        var id = MessageId.Auto(entries, 100);
        Assert.Equal(100, id.Timestamp);
        Assert.Equal(0, id.Sequence);
    }

    [Fact(DisplayName = "测试解析无效格式")]
    public void TestParseInvalid()
    {
        Assert.Throws<FormatException>(() => MessageId.Parse("invalid"));
        Assert.Throws<ArgumentNullException>(() => MessageId.Parse(null!));
    }

    [Fact(DisplayName = "测试 CompareTo null")]
    public void TestCompareToNull()
    {
        var id = new MessageId(100, 0);
        Assert.Equal(1, id.CompareTo(null));
    }

    [Fact(DisplayName = "测试 FluxEntry 消息 ID")]
    public void TestFluxEntryMessageId()
    {
        var entry = new FluxEntry { Timestamp = 12345, SequenceId = 3 };
        Assert.Equal("12345-3", entry.GetMessageId());

        var (ts, seq) = FluxEntry.ParseMessageId("12345-3");
        Assert.Equal(12345, ts);
        Assert.Equal(3, seq);
    }
}
