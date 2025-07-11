﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License
// See the LICENSE file in the project root for more information.
// Maintainer: Argo Zhang(argo@live.ca) Website: https://www.blazor.zone

using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;

namespace UnitTest.Services;

public class TcpSocketFactoryTest
{
    [Fact]
    public async Task GetOrCreate_Ok()
    {
        // 测试 GetOrCreate 方法创建的 Client 销毁后继续 GetOrCreate 得到的对象是否可用
        var sc = new ServiceCollection();
        sc.AddLogging(builder =>
        {
            builder.AddProvider(new MockLoggerProvider());
        });
        sc.AddBootstrapBlazorTcpSocketFactory();
        var provider = sc.BuildServiceProvider();
        var factory = provider.GetRequiredService<ITcpSocketFactory>();
        var client1 = factory.GetOrCreate("demo", op => op.LocalEndPoint = Utility.ConvertToIpEndPoint("localhost", 0));
        await client1.CloseAsync();

        var client2 = factory.GetOrCreate("demo", op => op.LocalEndPoint = Utility.ConvertToIpEndPoint("localhost", 0));
        Assert.Equal(client1, client2);

        var ip = Dns.GetHostAddresses(Dns.GetHostName(), AddressFamily.InterNetwork).FirstOrDefault() ?? IPAddress.Loopback;
        var client3 = factory.GetOrCreate("demo1", op => op.LocalEndPoint = Utility.ConvertToIpEndPoint(ip.ToString(), 0));

        // 测试不合格 IP 地址
        var client4 = factory.GetOrCreate("demo2", op => op.LocalEndPoint = Utility.ConvertToIpEndPoint("256.0.0.1", 0));

        var client5 = factory.Remove("demo2");
        Assert.Equal(client4, client5);
        Assert.NotNull(client5);

        await client5.DisposeAsync();
        await factory.DisposeAsync();
    }

    [Fact]
    public async Task ConnectAsync_Timeout()
    {
        var client = CreateClient();
        client.Options.ConnectTimeout = 1;

        var connect = await client.ConnectAsync("localhost", 9999);
        Assert.False(connect);
    }

    [Fact]
    public async Task ConnectAsync_Cancel()
    {
        var client = CreateClient();

        // 测试 ConnectAsync 方法连接取消逻辑
        var cst = new CancellationTokenSource();
        cst.Cancel();
        var connect = await client.ConnectAsync("localhost", 9999, cst.Token);
        Assert.False(connect);
    }

    [Fact]
    public async Task ConnectAsync_Failed()
    {
        var client = CreateClient();

        // 测试 ConnectAsync 方法连接失败
        var connect = await client.ConnectAsync("localhost", 9999);
        Assert.False(connect);
    }

    [Fact]
    public async Task ConnectAsync_Error()
    {
        var client = CreateClient();

        // 反射设置 SocketClientProvider 为空
        var propertyInfo = client.GetType().GetProperty("ServiceProvider", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(propertyInfo);
        propertyInfo.SetValue(client, null);

        // 测试 ConnectAsync 方法连接失败
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.ConnectAsync("localhost", 9999));
        Assert.NotNull(ex);

        // 反射测试 Log 方法
        var methodInfo = client.GetType().GetMethod("Log", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(methodInfo);
        methodInfo.Invoke(client, [LogLevel.Error, null!, "Test error log"]);
    }

    [Fact]
    public async Task Send_Timeout()
    {
        var port = 8887;
        var server = StartTcpServer(port, MockSplitPackageAsync);

        var client = CreateClient();
        client.Options.SendTimeout = 100;
        client.SetDataHandler(new MockSendTimeoutHandler());

        await client.ConnectAsync("localhost", port);

        var data = new ReadOnlyMemory<byte>([1, 2, 3, 4, 5]);
        var result = await client.SendAsync(data);
        Assert.False(result);
    }

    [Fact]
    public async Task SendAsync_Error()
    {
        var client = CreateClient();

        // 测试未建立连接前调用 SendAsync 方法报异常逻辑
        var data = new ReadOnlyMemory<byte>([1, 2, 3, 4, 5]);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.SendAsync(data));
        Assert.NotNull(ex);

        // 测试发送失败
        var port = 8892;
        var server = StartTcpServer(port, MockSplitPackageAsync);

        client.SetDataHandler(new MockSendErrorHandler());
        await client.ConnectAsync("localhost", port);
        Assert.True(client.IsConnected);

        // 内部生成异常日志
        await client.SendAsync(data);
    }

    [Fact]
    public async Task SendAsync_Cancel()
    {
        var port = 8881;
        var server = StartTcpServer(port, MockSplitPackageAsync);

        var client = CreateClient();
        Assert.False(client.IsConnected);

        // 连接 TCP Server
        await client.ConnectAsync("localhost", port);
        Assert.True(client.IsConnected);

        // 测试 SendAsync 方法发送取消逻辑
        var cst = new CancellationTokenSource();
        cst.Cancel();

        var result = await client.SendAsync("test", null, cst.Token);
        Assert.False(result);

        // 设置延时发送适配器
        // 延时发送期间关闭 Socket 连接导致内部报错
        client.SetDataHandler(new MockSendCancelHandler()
        {
            Socket = client
        });

        var tcs = new TaskCompletionSource();
        bool? sendResult = null;
        // 测试发送失败逻辑
        _ = Task.Run(async () =>
        {
            sendResult = await client.SendAsync("test", Encoding.UTF8);
            tcs.SetResult();
        });

        await tcs.Task;
        Assert.False(sendResult);

        // 关闭连接
        StopTcpServer(server);
    }

    [Fact]
    public async Task ReceiveAsync_Timeout()
    {
        var port = 8888;
        var server = StartTcpServer(port, MockSplitPackageAsync);

        var client = CreateClient();
        client.Options.ReceiveTimeout = 100;
        client.SetDataHandler(new MockReceiveTimeoutHandler());

        await client.ConnectAsync("localhost", port);

        var data = new ReadOnlyMemory<byte>([1, 2, 3, 4, 5]);
        await client.SendAsync(data);
        await Task.Delay(220); // 等待接收超时
    }

    [Fact]
    public async Task ReceiveAsync_Cancel()
    {
        var port = 8889;
        var server = StartTcpServer(port, MockSplitPackageAsync);

        var client = CreateClient();
        client.SetDataHandler(new MockReceiveTimeoutHandler());

        await client.ConnectAsync("localhost", port);

        var data = new ReadOnlyMemory<byte>([1, 2, 3, 4, 5]);
        await client.SendAsync(data);

        // 通过反射取消令牌
        var baseType = client.GetType().BaseType;
        Assert.NotNull(baseType);

        var fieldInfo = baseType.GetField("_receiveCancellationTokenSource", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(fieldInfo);
        var tokenSource = fieldInfo.GetValue(client) as CancellationTokenSource;
        Assert.NotNull(tokenSource);
        tokenSource.Cancel();
        await Task.Delay(50);
    }

    [Fact]
    public async Task ReceiveAsync_InvalidOperationException()
    {
        // 未连接时调用 ReceiveAsync 方法会抛出 InvalidOperationException 异常
        var client = CreateClient();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.ReceiveAsync());
        Assert.NotNull(ex);

        // 已连接但是启用了自动接收功能时调用 ReceiveAsync 方法会抛出 InvalidOperationException 异常
        var port = 8893;
        var server = StartTcpServer(port, MockSplitPackageAsync);

        client.Options.IsAutoReceive = true;
        var connected = await client.ConnectAsync("localhost", port);
        Assert.True(connected);

        ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.ReceiveAsync());
        Assert.NotNull(ex);
    }

    [Fact]
    public async Task ReceiveAsync_Ok()
    {
        var port = 8891;
        var server = StartTcpServer(port, MockSplitPackageAsync);

        var client = CreateClient();
        client.Options.IsAutoReceive = false;
        var connected = await client.ConnectAsync("localhost", port);
        Assert.True(connected);

        var data = new ReadOnlyMemory<byte>([1, 2, 3, 4, 5]);
        var send = await client.SendAsync(data);
        Assert.True(send);

        var payload = await client.ReceiveAsync();
        Assert.Equal(payload.ToArray(), [1, 2, 3, 4, 5]);
    }

    [Fact]
    public async Task ReceiveAsync_Error()
    {
        var client = CreateClient();

        // 测试未建立连接前调用 ReceiveAsync 方法报异常逻辑
        var baseType = client.GetType().BaseType;
        Assert.NotNull(baseType);

        var methodInfo = baseType.GetMethod("AutoReceiveAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(methodInfo);

        var task = (ValueTask)methodInfo.Invoke(client, null)!;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await task);
        Assert.NotNull(ex);

        var port = 8882;
        var server = StartTcpServer(port, MockSplitPackageAsync);

        Assert.Equal(1024 * 64, client.Options.ReceiveBufferSize);

        client.Options.ReceiveBufferSize = 1024 * 20;
        Assert.Equal(1024 * 20, client.Options.ReceiveBufferSize);

        client.SetDataHandler(new MockReceiveErrorHandler());

        ReadOnlyMemory<byte> buffer = ReadOnlyMemory<byte>.Empty;
        var tcs = new TaskCompletionSource();

        // 增加接收回调方法
        client.ReceivedCallBack = b =>
        {
            buffer = b;
            tcs.SetResult();
            return ValueTask.CompletedTask;
        };

        await client.ConnectAsync("localhost", port);

        // 发送数据导致接收数据异常
        var data = new ReadOnlyMemory<byte>([1, 2, 3, 4, 5]);
        await client.SendAsync(data);

        await tcs.Task;
        Assert.Equal(buffer.ToArray(), [1, 2, 3, 4, 5]);

        // 关闭连接
        StopTcpServer(server);
    }

    [Fact]
    public async Task CloseByRemote_Ok()
    {
        var client = CreateClient();

        var port = 8883;
        var server = StartTcpServer(port, MockAutoClosePackageAsync);

        client.SetDataHandler(new MockReceiveErrorHandler());

        // 连接 TCP Server
        await client.ConnectAsync("localhost", port);

        // 发送数据
        await client.SendAsync(new ReadOnlyMemory<byte>([1, 2, 3, 4, 5]));

        // 关闭连接
        StopTcpServer(server);
    }

    [Fact]
    public async Task FixLengthDataPackageHandler_Ok()
    {
        var port = 8884;
        var server = StartTcpServer(port, MockSplitPackageAsync);
        var client = CreateClient();

        // 测试 ConnectAsync 方法
        var connect = await client.ConnectAsync("localhost", port);
        Assert.True(connect);
        Assert.True(client.IsConnected);

        var tcs = new TaskCompletionSource();
        ReadOnlyMemory<byte> receivedBuffer = ReadOnlyMemory<byte>.Empty;

        // 增加数据处理适配器
        client.SetDataHandler(new FixLengthDataPackageHandler(7)
        {
            ReceivedCallBack = buffer =>
            {
                receivedBuffer = buffer;
                tcs.SetResult();
                return ValueTask.CompletedTask;
            }
        });

        // 测试 SendAsync 方法
        var data = new ReadOnlyMemory<byte>([1, 2, 3, 4, 5]);
        var result = await client.SendAsync(data);
        Assert.True(result);

        await tcs.Task;
        Assert.Equal(receivedBuffer.ToArray(), [1, 2, 3, 4, 5, 3, 4]);

        // 模拟延时等待内部继续读取逻辑完成，测试内部 _receiveCancellationTokenSource 取消逻辑
        await Task.Delay(10);

        // 关闭连接
        await client.CloseAsync();
        StopTcpServer(server);
    }

    [Fact]
    public async Task FixLengthDataPackageHandler_Sticky()
    {
        var port = 8885;
        var server = StartTcpServer(port, MockStickyPackageAsync);
        var client = CreateClient();

        // 连接 TCP Server
        var connect = await client.ConnectAsync("localhost", port);

        var tcs = new TaskCompletionSource();
        ReadOnlyMemory<byte> receivedBuffer = ReadOnlyMemory<byte>.Empty;

        // 增加数据库处理适配器
        client.SetDataHandler(new FixLengthDataPackageHandler(7)
        {
            ReceivedCallBack = buffer =>
            {
                receivedBuffer = buffer;
                tcs.SetResult();
                return ValueTask.CompletedTask;
            }
        });

        // 发送数据
        var data = new ReadOnlyMemory<byte>([1, 2, 3, 4, 5]);
        await client.SendAsync(data);

        // 等待接收数据处理完成
        await tcs.Task;

        // 验证接收到的数据
        Assert.Equal(receivedBuffer.ToArray(), [1, 2, 3, 4, 5, 3, 4]);
        receivedBuffer = ReadOnlyMemory<byte>.Empty;
        tcs = new TaskCompletionSource();

        // 等待第二次数据
        await tcs.Task;

        // 验证第二次收到的数据
        Assert.Equal(receivedBuffer.ToArray(), [2, 2, 3, 4, 5, 6, 7]);
        tcs = new TaskCompletionSource();
        await tcs.Task;

        // 验证第三次收到的数据
        Assert.Equal(receivedBuffer.ToArray(), [3, 2, 3, 4, 5, 6, 7]);

        // 关闭连接
        await client.CloseAsync();
        StopTcpServer(server);
    }

    [Fact]
    public async Task DelimiterDataPackageHandler_Ok()
    {
        var port = 8886;
        var server = StartTcpServer(port, MockDelimiterPackageAsync);
        var client = CreateClient();

        // 连接 TCP Server
        var connect = await client.ConnectAsync("localhost", port);

        var tcs = new TaskCompletionSource();
        ReadOnlyMemory<byte> receivedBuffer = ReadOnlyMemory<byte>.Empty;

        // 增加数据库处理适配器
        client.SetDataHandler(new DelimiterDataPackageHandler([0x13, 0x10])
        {
            ReceivedCallBack = buffer =>
            {
                receivedBuffer = buffer;
                tcs.SetResult();
                return ValueTask.CompletedTask;
            }
        });

        // 发送数据
        var data = new ReadOnlyMemory<byte>([1, 2, 3, 4, 5]);
        await client.SendAsync(data);

        // 等待接收数据处理完成
        await tcs.Task;

        // 验证接收到的数据
        Assert.Equal(receivedBuffer.ToArray(), [1, 2, 3, 4, 5, 0x13, 0x10]);

        // 等待第二次数据
        receivedBuffer = ReadOnlyMemory<byte>.Empty;
        tcs = new TaskCompletionSource();
        await tcs.Task;

        // 验证接收到的数据
        Assert.Equal(receivedBuffer.ToArray(), [5, 6, 0x13, 0x10]);

        // 关闭连接
        await client.CloseAsync();
        StopTcpServer(server);

        var handler = new DelimiterDataPackageHandler("\r\n");
        var ex = Assert.Throws<ArgumentNullException>(() => new DelimiterDataPackageHandler(string.Empty));
        Assert.NotNull(ex);

        ex = Assert.Throws<ArgumentNullException>(() => new DelimiterDataPackageHandler((byte[])null!));
        Assert.NotNull(ex);
    }

    private static TcpListener StartTcpServer(int port, Func<TcpClient, Task> handler)
    {
        var server = new TcpListener(IPAddress.Loopback, port);
        server.Start();
        Task.Run(() => AcceptClientsAsync(server, handler));
        return server;
    }

    private static async Task AcceptClientsAsync(TcpListener server, Func<TcpClient, Task> handler)
    {
        while (true)
        {
            var client = await server.AcceptTcpClientAsync();
            _ = Task.Run(() => handler(client));
        }
    }

    private static async Task MockDelimiterPackageAsync(TcpClient client)
    {
        using var stream = client.GetStream();
        while (true)
        {
            var buffer = new byte[10240];
            var len = await stream.ReadAsync(buffer);
            if (len == 0)
            {
                break;
            }

            // 回写数据到客户端
            var block = new ReadOnlyMemory<byte>(buffer, 0, len);
            await stream.WriteAsync(block, CancellationToken.None);

            await Task.Delay(20);

            // 模拟拆包发送第二段数据
            await stream.WriteAsync(new byte[] { 0x13, 0x10, 0x5, 0x6, 0x13, 0x10 }, CancellationToken.None);
        }
    }

    private static async Task MockSplitPackageAsync(TcpClient client)
    {
        using var stream = client.GetStream();
        while (true)
        {
            var buffer = new byte[1024];
            var len = await stream.ReadAsync(buffer);
            if (len == 0)
            {
                break;
            }

            // 回写数据到客户端
            var block = new ReadOnlyMemory<byte>(buffer, 0, len);
            await stream.WriteAsync(block, CancellationToken.None);

            // 模拟延时
            await Task.Delay(50);

            // 模拟拆包发送第二段数据
            await stream.WriteAsync(new byte[] { 0x3, 0x4 }, CancellationToken.None);
        }
    }

    private static async Task MockStickyPackageAsync(TcpClient client)
    {
        using var stream = client.GetStream();
        while (true)
        {
            var buffer = new byte[10240];
            var len = await stream.ReadAsync(buffer);
            if (len == 0)
            {
                break;
            }

            // 回写数据到客户端
            var block = new ReadOnlyMemory<byte>(buffer, 0, len);
            await stream.WriteAsync(block, CancellationToken.None);

            // 模拟延时
            await Task.Delay(10);

            // 模拟拆包发送第二段数据
            await stream.WriteAsync(new byte[] { 0x3, 0x4, 0x2, 0x2 }, CancellationToken.None);

            // 模拟延时
            await Task.Delay(10);

            // 模拟粘包发送后续数据
            await stream.WriteAsync(new byte[] { 0x3, 0x4, 0x5, 0x6, 0x7, 0x3, 0x2, 0x3, 0x4, 0x5, 0x6, 0x7, 0x1 }, CancellationToken.None);
        }
    }

    private static Task MockAutoClosePackageAsync(TcpClient client)
    {
        client.Close();
        return Task.CompletedTask;
    }

    private static void StopTcpServer(TcpListener server)
    {
        server?.Stop();
    }

    private static ITcpSocketClient CreateClient()
    {
        var sc = new ServiceCollection();
        sc.AddLogging(builder =>
        {
            builder.AddProvider(new MockLoggerProvider());
        });
        sc.AddBootstrapBlazorTcpSocketFactory();
        var provider = sc.BuildServiceProvider();
        var factory = provider.GetRequiredService<ITcpSocketFactory>();
        var client = factory.GetOrCreate("test", op => op.LocalEndPoint = Utility.ConvertToIpEndPoint("localhost", 0));
        return client;
    }

    class MockLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName)
        {
            return new MockLogger();
        }

        public void Dispose()
        {

        }
    }

    class MockLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {

        }
    }

    class MockSendErrorHandler : DataPackageHandlerBase
    {
        public ITcpSocketClient? Socket { get; set; }

        public override ValueTask<ReadOnlyMemory<byte>> SendAsync(ReadOnlyMemory<byte> data, CancellationToken token = default)
        {
            throw new Exception("Mock send failed");
        }
    }

    class MockSendCancelHandler : DataPackageHandlerBase
    {
        public ITcpSocketClient? Socket { get; set; }

        public override async ValueTask<ReadOnlyMemory<byte>> SendAsync(ReadOnlyMemory<byte> data, CancellationToken token = default)
        {
            if (Socket != null)
            {
                await Socket.CloseAsync();
            }
            await Task.Delay(10, token);
            return data;
        }
    }

    class MockReceiveErrorHandler : DataPackageHandlerBase
    {
        public override ValueTask<ReadOnlyMemory<byte>> SendAsync(ReadOnlyMemory<byte> data, CancellationToken token = default)
        {
            return ValueTask.FromResult(data);
        }

        public override async ValueTask ReceiveAsync(ReadOnlyMemory<byte> data, CancellationToken token = default)
        {
            await base.ReceiveAsync(data, token);

            // 模拟接收数据时报错
            throw new InvalidOperationException("Test Error");
        }
    }

    class MockSendTimeoutHandler : DataPackageHandlerBase
    {
        public override async ValueTask<ReadOnlyMemory<byte>> SendAsync(ReadOnlyMemory<byte> data, CancellationToken token = default)
        {
            // 模拟发送超时
            await Task.Delay(200, token);
            return data;
        }
    }

    class MockReceiveTimeoutHandler : DataPackageHandlerBase
    {
        public override async ValueTask ReceiveAsync(ReadOnlyMemory<byte> data, CancellationToken token = default)
        {
            // 模拟接收超时
            await Task.Delay(200, token);
            await base.ReceiveAsync(data, token);
        }
    }
}
