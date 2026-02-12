using System.Text;
using LeaseGate.Protocol;

namespace LeaseGate.Tests;

public class PipeFramingSecurityTests
{
    [Fact]
    public async Task ReadAsync_RejectsZeroLength()
    {
        var stream = new MemoryStream(BitConverter.GetBytes(0));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => PipeMessageFraming.ReadAsync<object>(stream, CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_RejectsNegativeLength()
    {
        var stream = new MemoryStream(BitConverter.GetBytes(-1));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => PipeMessageFraming.ReadAsync<object>(stream, CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_RejectsOversizedPayload()
    {
        var oversized = 16 * 1024 * 1024 + 1;
        var stream = new MemoryStream(BitConverter.GetBytes(oversized));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => PipeMessageFraming.ReadAsync<object>(stream, CancellationToken.None));
        Assert.Contains("exceeds maximum", ex.Message);
    }

    [Fact]
    public async Task ReadAsync_AcceptsPayloadUnderMax()
    {
        // Test with a small valid payload to ensure the size check doesn't reject valid data
        // (We test the boundary at 16MB+1 in RejectsOversizedPayload above; testing exactly 16MB
        // is impractical due to memory/time, so we verify a normal-sized payload passes)
        var json = "{\"command\":\"GetStatus\",\"payloadJson\":\"{}\"}";
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        var ms = new MemoryStream();
        ms.Write(BitConverter.GetBytes(jsonBytes.Length));
        ms.Write(jsonBytes);
        ms.Position = 0;

        var result = await PipeMessageFraming.ReadAsync<PipeCommandRequest>(ms, CancellationToken.None);
        Assert.Equal("GetStatus", result.Command);
    }

    [Fact]
    public async Task WriteAndRead_RoundtripsCorrectly()
    {
        var original = new PipeCommandRequest
        {
            Command = "GetStatus",
            PayloadJson = "{\"test\": true}"
        };

        var ms = new MemoryStream();
        await PipeMessageFraming.WriteAsync(ms, original, CancellationToken.None);
        ms.Position = 0;

        var result = await PipeMessageFraming.ReadAsync<PipeCommandRequest>(ms, CancellationToken.None);
        Assert.Equal("GetStatus", result.Command);
        Assert.Equal("{\"test\": true}", result.PayloadJson);
    }
}
