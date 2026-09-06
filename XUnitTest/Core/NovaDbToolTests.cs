using System;
using System.IO;
using NewLife.NovaDb.Core;
using NewLife.NovaDb.Sql;
using Xunit;

namespace XUnitTest.Core;

/// <summary>NovaDbTool 管理工具测试</summary>
public class NovaDbToolTests : IDisposable
{
    private readonly String _testDir;
    private readonly SqlEngine _engine;

    public NovaDbToolTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"NovaDbToolTests_{Guid.NewGuid():N}");
        _engine = new SqlEngine(_testDir, new DbOptions { Path = _testDir, WalMode = WalMode.None });
    }

    public void Dispose()
    {
        _engine.Dispose();

        if (Directory.Exists(_testDir))
        {
            try
            {
                Directory.Delete(_testDir, recursive: true);
            }
            catch
            {
                // 忽略清理错误
            }
        }
    }

    [Fact(DisplayName = "测试检查完整性-有效目录")]
    public void TestCheckIntegrityValid()
    {
        Assert.True(NovaDbTool.CheckIntegrity(_testDir));
    }

    [Fact(DisplayName = "测试检查完整性-无效目录")]
    public void TestCheckIntegrityInvalid()
    {
        var fakePath = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}");
        Assert.False(NovaDbTool.CheckIntegrity(fakePath));
    }

    [Fact(DisplayName = "测试获取版本")]
    public void TestGetVersion()
    {
        var version = NovaDbTool.GetVersion();

        Assert.NotNull(version);
        Assert.NotEmpty(version);
    }

    [Fact(DisplayName = "测试获取状态摘要")]
    public void TestGetStatus()
    {
        _engine.Execute("CREATE TABLE users (id INT PRIMARY KEY, name VARCHAR)");
        _engine.Execute("INSERT INTO users VALUES (1, 'Alice')");

        var status = NovaDbTool.GetStatus(_engine);

        Assert.Contains("NovaDb", status);
        Assert.Contains("Tables:", status);
        Assert.Contains("Executes:", status);
    }

    [Fact(DisplayName = "测试检查完整性-空参数")]
    public void TestCheckIntegrityNull()
    {
        Assert.Throws<ArgumentNullException>(() => NovaDbTool.CheckIntegrity(null!));
    }

    [Fact(DisplayName = "测试获取状态-空参数")]
    public void TestGetStatusNull()
    {
        Assert.Throws<ArgumentNullException>(() => NovaDbTool.GetStatus(null!));
    }
}
