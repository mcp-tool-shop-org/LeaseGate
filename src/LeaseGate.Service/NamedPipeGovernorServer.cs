using System.IO.Pipes;
using LeaseGate.Protocol;

namespace LeaseGate.Service;

public sealed class NamedPipeGovernorServer : IDisposable
{
    private readonly LeaseGovernor _governor;
    private readonly string _pipeName;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public NamedPipeGovernorServer(LeaseGovernor governor, string pipeName)
    {
        _governor = governor;
        _pipeName = pipeName;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        if (_cts is null)
        {
            return;
        }

        _cts.Cancel();
        if (_listenTask is not null)
        {
            try
            {
                await _listenTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var server = new NamedPipeServerStream(
                _pipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            await server.WaitForConnectionAsync(cancellationToken);
            await HandleConnectionAsync(server, cancellationToken);
        }
    }

    private async Task HandleConnectionAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            var request = await PipeMessageFraming.ReadAsync<PipeCommandRequest>(stream, cancellationToken);
            PipeCommandResponse response;

            switch (request.Command)
            {
                case "Acquire":
                {
                    var payload = ProtocolJson.Deserialize<AcquireLeaseRequest>(request.PayloadJson);
                    var result = await _governor.AcquireAsync(payload, cancellationToken);
                    response = new PipeCommandResponse
                    {
                        Success = true,
                        PayloadJson = ProtocolJson.Serialize(result)
                    };
                    break;
                }
                case "Release":
                {
                    var payload = ProtocolJson.Deserialize<ReleaseLeaseRequest>(request.PayloadJson);
                    var result = await _governor.ReleaseAsync(payload, cancellationToken);
                    response = new PipeCommandResponse
                    {
                        Success = true,
                        PayloadJson = ProtocolJson.Serialize(result)
                    };
                    break;
                }
                default:
                    response = new PipeCommandResponse
                    {
                        Success = false,
                        Error = "unknown_command"
                    };
                    break;
            }

            await PipeMessageFraming.WriteAsync(stream, response, cancellationToken);
        }
        catch (Exception ex)
        {
            var error = new PipeCommandResponse
            {
                Success = false,
                Error = ex.Message
            };
            await PipeMessageFraming.WriteAsync(stream, error, cancellationToken);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
