using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NewLife.NovaDb.Core;
using NewLife.NovaDb.Engine.Flux;
using NewLife.NovaDb.Queues;
using Xunit;

#nullable enable

namespace XUnitTest.Queues;

/// <summary>NovaQueue 单元测试</summary>
public class NovaQueueTests : IDisposable
{
    private readonly String _testDir;

    public NovaQueueTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"NovaQueueTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, recursive: true); }
            catch { }
        }
    }

    private FluxEngine CreateEngine()
    {
        var options = new DbOptions { FluxPartitionHours = 1 };
        return new FluxEngine(_testDir, options);
    }

    [Fact(DisplayName = "测试生产和消费消息")]
    public void TestAddAndTake()
    {
        using var engine = CreateEngine();
        var queue = new NovaQueue<String>(engine, "test-topic");
        queue.SetGroup("test-group");

        // 生产消息
        var added = queue.Add("hello", "world");
        Assert.Equal(2, added);

        // 消费消息
        var messages = queue.Take(10).ToList();
        Assert.Equal(2, messages.Count);
        Assert.Equal("hello", messages[0]);
        Assert.Equal("world", messages[1]);
    }

    [Fact(DisplayName = "测试消息计数")]
    public void TestCount()
    {
        using var engine = CreateEngine();
        var queue = new NovaQueue<String>(engine, "test-topic");

        Assert.True(queue.IsEmpty);
        Assert.Equal(0, queue.Count);

        queue.Add("msg1");
        Assert.False(queue.IsEmpty);
    }

    [Fact(DisplayName = "测试确认消息")]
    public void TestAcknowledge()
    {
        using var engine = CreateEngine();
        var queue = new NovaQueue<String>(engine, "test-topic");
        queue.SetGroup("ack-group");

        queue.Add("msg1");

        var messages = queue.Take(1).ToList();
        Assert.Single(messages);

        // 从 pending 中获取 ID 进行确认
        var pending = queue.GetPendingEntries("ack-group");
        Assert.NotEmpty(pending);

        var count = queue.Acknowledge(pending[0].Id.ToString());
        Assert.Equal(1, count);
    }

    [Fact(DisplayName = "测试SetGroup创建消费组")]
    public void TestSetGroup()
    {
        using var engine = CreateEngine();
        var queue = new NovaQueue<String>(engine, "test-topic");
        Assert.True(queue.SetGroup("my-group"));

        var groups = queue.GetConsumerGroupNames();
        Assert.Contains("my-group", groups);
    }

    [Fact(DisplayName = "测试主题名称")]
    public void TestTopic()
    {
        using var engine = CreateEngine();
        var queue = new NovaQueue<String>(engine, "my-topic");
        Assert.Equal("my-topic", queue.Topic);
    }

    [Fact(DisplayName = "测试异步消费循环")]
    public async Task TestConsumeAsync()
    {
        using var engine = CreateEngine();
        var queue = new NovaQueue<String>(engine, "consume-topic");
        queue.SetGroup("consume-group");
        queue.BlockTime = 1;

        var received = new List<String>();
        var cts = new CancellationTokenSource();

        // 先发布消息
        queue.Add("msg1", "msg2");

        // 启动消费循环
        var consumeTask = queue.ConsumeAsync(msg => received.Add(msg), cts.Token);

        // 等待消费
        await Task.Delay(2000);
        cts.Cancel();

        try { await consumeTask; } catch (OperationCanceledException) { }

        Assert.Contains("msg1", received);
        Assert.Contains("msg2", received);
    }

    [Fact(DisplayName = "测试整数类型消息")]
    public void TestIntMessages()
    {
        using var engine = CreateEngine();
        var queue = new NovaQueue<Int32>(engine, "int-topic");
        queue.SetGroup("int-group");

        queue.Add(1, 2, 3);

        var messages = queue.Take(10).ToList();
        Assert.Equal(3, messages.Count);
        Assert.Equal(1, messages[0]);
        Assert.Equal(2, messages[1]);
        Assert.Equal(3, messages[2]);
    }

    [Fact(DisplayName = "测试无消费组时Take抛出异常")]
    public void TestTakeWithoutGroup()
    {
        using var engine = CreateEngine();
        var queue = new NovaQueue<String>(engine, "test-topic");

        Assert.Throws<InvalidOperationException>(() => queue.Take(1).ToList());
    }

    #region 消费组管理测试
    [Fact(DisplayName = "测试创建消费组")]
    public void TestCreateConsumerGroup()
    {
        using var engine = CreateEngine();
        var queue = new NovaQueue<String>(engine, "test-topic");

        queue.CreateConsumerGroup("group1");
        queue.CreateConsumerGroup("group2");

        var names = queue.GetConsumerGroupNames();
        Assert.Equal(2, names.Count);
        Assert.Contains("group1", names);
        Assert.Contains("group2", names);
    }

    [Fact(DisplayName = "测试消费组读取")]
    public void TestReadGroup()
    {
        using var engine = CreateEngine();
        var queue = new NovaQueue<String>(engine, "test-topic");

        var now = DateTime.UtcNow.Ticks;

        // 发布 5 条消息
        for (var i = 0; i < 5; i++)
        {
            queue.Publish(new FluxEntry
            {
                Timestamp = now + i,
                Fields = new Dictionary<String, Object?> { ["v"] = i }
            });
        }

        queue.CreateConsumerGroup("g1");

        // 读取 3 条
        var messages = queue.ReadGroup("g1", "consumer1", 3);
        Assert.Equal(3, messages.Count);

        // 再读取剩余
        var remaining = queue.ReadGroup("g1", "consumer1", 10);
        Assert.Equal(2, remaining.Count);

        // 没有更多消息
        var empty = queue.ReadGroup("g1", "consumer1", 10);
        Assert.Empty(empty);
    }

    [Fact(DisplayName = "测试确认消息（消费组级别）")]
    public void TestAcknowledgeGroup()
    {
        using var engine = CreateEngine();
        var queue = new NovaQueue<String>(engine, "test-topic");

        var now = DateTime.UtcNow.Ticks;
        var id = queue.Publish(new FluxEntry
        {
            Timestamp = now,
            Fields = new Dictionary<String, Object?> { ["v"] = 1 }
        });

        queue.CreateConsumerGroup("g1");
        queue.ReadGroup("g1", "consumer1", 1);

        // 确认前有 1 条 pending
        var pending = queue.GetPendingEntries("g1");
        Assert.Single(pending);

        // 确认消息
        var acked = queue.Acknowledge("g1", id);
        Assert.True(acked);

        // 确认后 pending 为空
        pending = queue.GetPendingEntries("g1");
        Assert.Empty(pending);
    }

    [Fact(DisplayName = "测试待确认条目")]
    public void TestPendingEntries()
    {
        using var engine = CreateEngine();
        var queue = new NovaQueue<String>(engine, "test-topic");

        var now = DateTime.UtcNow.Ticks;

        for (var i = 0; i < 3; i++)
        {
            queue.Publish(new FluxEntry
            {
                Timestamp = now + i,
                Fields = new Dictionary<String, Object?> { ["v"] = i }
            });
        }

        queue.CreateConsumerGroup("g1");
        queue.ReadGroup("g1", "consumer1", 3);

        var pending = queue.GetPendingEntries("g1");
        Assert.Equal(3, pending.Count);

        // 每条 pending 都有消费者信息
        foreach (var p in pending)
        {
            Assert.Equal("consumer1", p.Consumer);
            Assert.Equal(1, p.DeliveryCount);
            Assert.NotNull(p.Id);
        }
    }

    [Fact(DisplayName = "测试消费组不存在异常")]
    public void TestConsumerGroupNotFound()
    {
        using var engine = CreateEngine();
        var queue = new NovaQueue<String>(engine, "test-topic");

        var ex = Assert.Throws<NovaException>(() => queue.ReadGroup("nonexistent", "c1", 1));
        Assert.Equal(ErrorCode.ConsumerGroupNotFound, ex.Code);
    }

    [Fact(DisplayName = "测试多消费组独立消费")]
    public void TestMultipleConsumerGroups()
    {
        using var engine = CreateEngine();
        var queue = new NovaQueue<String>(engine, "test-topic");

        var now = DateTime.UtcNow.Ticks;

        for (var i = 0; i < 3; i++)
        {
            queue.Publish(new FluxEntry
            {
                Timestamp = now + i,
                Fields = new Dictionary<String, Object?> { ["v"] = i }
            });
        }

        queue.CreateConsumerGroup("g1");
        queue.CreateConsumerGroup("g2");

        // 两个组各自独立读取
        var g1Messages = queue.ReadGroup("g1", "c1", 10);
        var g2Messages = queue.ReadGroup("g2", "c2", 10);

        Assert.Equal(3, g1Messages.Count);
        Assert.Equal(3, g2Messages.Count);
    }
    #endregion

    #region 阻塞读取测试
    [Fact(DisplayName = "阻塞读取-有消息时立即返回")]
    public async Task TestBlockingReadWithAvailableMessages()
    {
        using var engine = CreateEngine();
        var queue = new NovaQueue<String>(engine, "test-topic");

        var now = DateTime.UtcNow.Ticks;
        queue.Publish(new FluxEntry
        {
            Timestamp = now,
            Fields = new Dictionary<String, Object?> { ["v"] = 1 }
        });

        queue.CreateConsumerGroup("g1");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await queue.ReadGroupAsync("g1", "c1", 10, TimeSpan.FromSeconds(5));
        sw.Stop();

        Assert.Single(result);
        // 有消息时应立即返回，不应等待
        Assert.True(sw.ElapsedMilliseconds < 1000);
    }

    [Fact(DisplayName = "阻塞读取-超时返回空列表")]
    public async Task TestBlockingReadTimeout()
    {
        using var engine = CreateEngine();
        var queue = new NovaQueue<String>(engine, "test-topic");

        queue.CreateConsumerGroup("g1");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await queue.ReadGroupAsync("g1", "c1", 10, TimeSpan.FromMilliseconds(200));
        sw.Stop();

        Assert.Empty(result);
        // 应等待约 200ms
        Assert.True(sw.ElapsedMilliseconds >= 150);
    }

    [Fact(DisplayName = "阻塞读取-取消令牌")]
    public async Task TestBlockingReadCancellation()
    {
        using var engine = CreateEngine();
        var queue = new NovaQueue<String>(engine, "test-topic");

        queue.CreateConsumerGroup("g1");

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await queue.ReadGroupAsync("g1", "c1", 10, TimeSpan.FromSeconds(10), cts.Token);
        });
    }

    [Fact(DisplayName = "阻塞读取-新消息唤醒")]
    public async Task TestBlockingReadWakesOnPublish()
    {
        using var engine = CreateEngine();
        var queue = new NovaQueue<String>(engine, "test-topic");

        queue.CreateConsumerGroup("g1");

        // 后台延迟发布消息
        var publishTask = Task.Run(async () =>
        {
            await Task.Delay(200);
            queue.Publish(new FluxEntry
            {
                Timestamp = DateTime.UtcNow.Ticks,
                Fields = new Dictionary<String, Object?> { ["v"] = "wakeup" }
            });
        });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await queue.ReadGroupAsync("g1", "c1", 10, TimeSpan.FromSeconds(5));
        sw.Stop();

        await publishTask;

        Assert.Single(result);
        // 应在约 200ms 后被唤醒，而不是等待 5 秒
        Assert.True(sw.ElapsedMilliseconds < 3000);
    }
    #endregion

    #region 延迟消息测试
    [Fact(DisplayName = "延迟消息-发布和投递")]
    public void TestDelayedMessagePublishAndDelivery()
    {
        using var engine = CreateEngine();
        var queue = new NovaQueue<String>(engine, "test-topic");

        var now = DateTime.UtcNow.Ticks;
        var entry = new FluxEntry
        {
            Timestamp = now,
            Fields = new Dictionary<String, Object?> { ["v"] = "delayed" }
        };

        // 发布延迟消息（延迟 0 秒，立即可投递）
        queue.PublishDelayed(entry, TimeSpan.Zero);

        // 尚未投递，引擎中无条目
        Assert.Equal(0, engine.GetEntryCount());
        Assert.Equal(1, queue.GetDelayedMessageCount());

        // 投递到期消息
        var delivered = queue.DeliverDueMessages();
        Assert.Equal(1, delivered);
        Assert.Equal(1, engine.GetEntryCount());
        Assert.Equal(0, queue.GetDelayedMessageCount());
    }

    [Fact(DisplayName = "定时投递-过去时间立即投递")]
    public void TestScheduledMessageDelivery()
    {
        using var engine = CreateEngine();
        var queue = new NovaQueue<String>(engine, "test-topic");

        var entry = new FluxEntry
        {
            Timestamp = DateTime.UtcNow.Ticks,
            Fields = new Dictionary<String, Object?> { ["v"] = "scheduled" }
        };

        // 投递时间设为过去
        queue.PublishScheduled(entry, DateTime.UtcNow.AddSeconds(-1));

        // 投递到期消息
        var delivered = queue.DeliverDueMessages();
        Assert.Equal(1, delivered);
        Assert.Equal(1, engine.GetEntryCount());
    }

    [Fact(DisplayName = "延迟消息-未到期不投递")]
    public void TestDelayedMessageNotDeliveredBeforeDue()
    {
        using var engine = CreateEngine();
        var queue = new NovaQueue<String>(engine, "test-topic");

        var entry = new FluxEntry
        {
            Timestamp = DateTime.UtcNow.Ticks,
            Fields = new Dictionary<String, Object?> { ["v"] = "future" }
        };

        // 延迟 1 小时
        queue.PublishDelayed(entry, TimeSpan.FromHours(1));

        var delivered = queue.DeliverDueMessages();
        Assert.Equal(0, delivered);
        Assert.Equal(0, engine.GetEntryCount());
        Assert.Equal(1, queue.GetDelayedMessageCount());
    }

    [Fact(DisplayName = "延迟消息-ReadGroup自动投递到期消息")]
    public void TestDelayedMessageAutoDeliverOnRead()
    {
        using var engine = CreateEngine();
        var queue = new NovaQueue<String>(engine, "test-topic");

        var entry = new FluxEntry
        {
            Timestamp = DateTime.UtcNow.Ticks,
            Fields = new Dictionary<String, Object?> { ["v"] = "auto-deliver" }
        };

        // 投递时间设为过去，消息即将到期
        queue.PublishScheduled(entry, DateTime.UtcNow.AddSeconds(-1));

        queue.CreateConsumerGroup("g1");

        // ReadGroup 内部会调用 DeliverDueMessages
        var messages = queue.ReadGroup("g1", "c1", 10);
        Assert.Single(messages);
    }
    #endregion

    #region 死信队列测试
    [Fact(DisplayName = "死信队列-手动移入和读取")]
    public void TestDeadLetterManualMoveAndRead()
    {
        using var engine = CreateEngine();
        var queue = new NovaQueue<String>(engine, "test-topic");

        var now = DateTime.UtcNow.Ticks;
        var id = queue.Publish(new FluxEntry
        {
            Timestamp = now,
            Fields = new Dictionary<String, Object?> { ["v"] = 1 }
        });

        queue.CreateConsumerGroup("g1");
        queue.ReadGroup("g1", "c1", 1);

        // 移入死信队列
        queue.MoveToDeadLetter("g1", id, "处理失败");

        // 原 pending 应被清除
        var pending = queue.GetPendingEntries("g1");
        Assert.Empty(pending);

        // 读取死信
        var deadLetters = queue.ReadDeadLetters("g1");
        Assert.Single(deadLetters);
        Assert.Equal(id, deadLetters[0].Id);
        Assert.Equal("处理失败", deadLetters[0].Reason);
        Assert.Equal(1, deadLetters[0].FailureCount);
    }

    [Fact(DisplayName = "死信队列-重新投递")]
    public void TestDeadLetterRetry()
    {
        using var engine = CreateEngine();
        var queue = new NovaQueue<String>(engine, "test-topic");

        var now = DateTime.UtcNow.Ticks;
        var id = queue.Publish(new FluxEntry
        {
            Timestamp = now,
            Fields = new Dictionary<String, Object?> { ["v"] = 1 }
        });

        queue.CreateConsumerGroup("g1");
        queue.ReadGroup("g1", "c1", 1);

        // 移入死信队列
        queue.MoveToDeadLetter("g1", id, "error");

        // 重新投递
        var retried = queue.RetryDeadLetter("g1", id);
        Assert.True(retried);

        // 死信队列应为空
        var deadLetters = queue.ReadDeadLetters("g1");
        Assert.Empty(deadLetters);

        // 新消息应可被消费
        Assert.Equal(2, engine.GetEntryCount());
    }

    [Fact(DisplayName = "死信队列-删除死信")]
    public void TestDeadLetterDelete()
    {
        using var engine = CreateEngine();
        var queue = new NovaQueue<String>(engine, "test-topic");

        var now = DateTime.UtcNow.Ticks;
        var id = queue.Publish(new FluxEntry
        {
            Timestamp = now,
            Fields = new Dictionary<String, Object?> { ["v"] = 1 }
        });

        queue.CreateConsumerGroup("g1");
        queue.ReadGroup("g1", "c1", 1);
        queue.MoveToDeadLetter("g1", id, "error");

        var deleted = queue.DeleteDeadLetter("g1", id);
        Assert.True(deleted);

        var deadLetters = queue.ReadDeadLetters("g1");
        Assert.Empty(deadLetters);

        // 删除不存在的死信应返回 false
        var deletedAgain = queue.DeleteDeadLetter("g1", id);
        Assert.False(deletedAgain);
    }

    [Fact(DisplayName = "死信队列-ProcessExpiredPending按重试次数移入")]
    public void TestProcessExpiredPendingByRetryCount()
    {
        using var engine = CreateEngine();
        var queue = new NovaQueue<String>(engine, "test-topic");
        queue.MaxRetryCount = 2;

        var now = DateTime.UtcNow.Ticks;
        var id = queue.Publish(new FluxEntry
        {
            Timestamp = now,
            Fields = new Dictionary<String, Object?> { ["v"] = 1 }
        });

        queue.CreateConsumerGroup("g1");
        queue.ReadGroup("g1", "c1", 1);

        // 模拟多次投递：通过 maxAge=0 测试
        var pending = queue.GetPendingEntries("g1");
        Assert.Single(pending);
        Assert.Equal(1, pending[0].DeliveryCount);

        var moved = queue.ProcessExpiredPending("g1", TimeSpan.Zero);

        // maxAge=0 表示所有 pending 都已过期
        Assert.Equal(1, moved);

        var deadLetters = queue.ReadDeadLetters("g1");
        Assert.Single(deadLetters);
        Assert.Contains("age exceeded", deadLetters[0].Reason);
    }

    [Fact(DisplayName = "死信队列-ProcessExpiredPending按年龄移入")]
    public void TestProcessExpiredPendingByAge()
    {
        using var engine = CreateEngine();
        var queue = new NovaQueue<String>(engine, "test-topic");

        var now = DateTime.UtcNow.Ticks;

        for (var i = 0; i < 3; i++)
        {
            queue.Publish(new FluxEntry
            {
                Timestamp = now + i,
                Fields = new Dictionary<String, Object?> { ["v"] = i }
            });
        }

        queue.CreateConsumerGroup("g1");
        queue.ReadGroup("g1", "c1", 3);

        // 所有消息 age > 0，使用 maxAge=0 应全部移入死信
        var moved = queue.ProcessExpiredPending("g1", TimeSpan.Zero);
        Assert.Equal(3, moved);

        var pending = queue.GetPendingEntries("g1");
        Assert.Empty(pending);

        var deadLetters = queue.ReadDeadLetters("g1");
        Assert.Equal(3, deadLetters.Count);
    }

    [Fact(DisplayName = "死信队列-未过期不处理")]
    public void TestProcessExpiredPendingNotExpired()
    {
        using var engine = CreateEngine();
        var queue = new NovaQueue<String>(engine, "test-topic");

        var now = DateTime.UtcNow.Ticks;
        queue.Publish(new FluxEntry
        {
            Timestamp = now,
            Fields = new Dictionary<String, Object?> { ["v"] = 1 }
        });

        queue.CreateConsumerGroup("g1");
        queue.ReadGroup("g1", "c1", 1);

        // 使用足够长的 maxAge，消息不应被移入死信
        var moved = queue.ProcessExpiredPending("g1", TimeSpan.FromHours(1));
        Assert.Equal(0, moved);

        var pending = queue.GetPendingEntries("g1");
        Assert.Single(pending);
    }
    #endregion
}
