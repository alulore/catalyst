using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Shouldly;

namespace Catalyst.Tests.Services;

public class SourceResolverTests
{
    [Theory]
    [InlineData("https://api.example.com/openapi.json", SourceType.Url)]
    [InlineData("http://localhost/swagger.json", SourceType.Url)]
    [InlineData("./swagger.json", SourceType.FilePath)]
    [InlineData("C:\\temp\\spec.json", SourceType.FilePath)]
    internal void DetermineSourceType_ClassifiesCorrectly(string source, SourceType expected)
    {
        SourceResolver.DetermineSourceType(source).ShouldBe(expected);
    }

    [Fact]
    public async Task Resolve_ReturnsError_ForMissingFile()
    {
        Result<SourceResolveResult> result = await SourceResolver.ResolveAsync(
            "./does-not-exist-12345.json",
            CancellationToken.None
        );

        result.IsSuccess.ShouldBeFalse();
        string.IsNullOrWhiteSpace(result.ErrorMessage).ShouldBeFalse();
    }

    [Fact]
    public async Task Resolve_ReturnsContent_ForExistingFile()
    {
        string path = Path.Combine(Path.GetTempPath(), "catalyst-resolver-test.json");
        await File.WriteAllTextAsync(
            path,
            "{ \"hello\": \"world\" }",
            TestContext.Current.CancellationToken
        );

        try
        {
            Result<SourceResolveResult> result = await SourceResolver.ResolveAsync(
                path,
                CancellationToken.None
            );

            result.IsSuccess.ShouldBeTrue();
            result.Value.ShouldNotBeNull().Type.ShouldBe(SourceType.FilePath);
            result.Value.ShouldNotBeNull().Content.ShouldContain("hello", Case.Sensitive);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Resolve_RejectsSelfSignedCertificate_ByDefault()
    {
        await using SelfSignedHttpsServer server = new(
            """{"openapi":"3.0.3","info":{"title":"t","version":"1"},"paths":{}}"""
        );

        Result<SourceResolveResult> result = await SourceResolver.ResolveAsync(
            $"https://localhost:{server.Port}/openapi.json",
            CancellationToken.None
        );

        result.IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public async Task Resolve_AcceptsSelfSignedCertificate_WhenInsecure()
    {
        const string spec = """{"openapi":"3.0.3","info":{"title":"t","version":"1"},"paths":{}}""";
        await using SelfSignedHttpsServer server = new(spec);

        Result<SourceResolveResult> result = await SourceResolver.ResolveAsync(
            $"https://localhost:{server.Port}/openapi.json",
            insecure: true,
            cancellationToken: CancellationToken.None
        );

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotBeNull().Content.ShouldBe(spec);
    }

    /// <summary>
    /// Minimal single-request HTTPS server that presents a freshly generated
    /// self-signed certificate, so certificate validation failures can be exercised.
    /// </summary>
    private sealed class SelfSignedHttpsServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly X509Certificate2 _certificate;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _acceptLoop;

        public SelfSignedHttpsServer(string body)
        {
            Body = body;
            _certificate = CreateSelfSignedCertificate();
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token), _cts.Token);
        }

        public string Body { get; }

        public int Port { get; }

        public Exception? LastClientFailure { get; private set; }

        /// <summary>Round-trip through PKCS#12 so the private key is usable by SslStream on all platforms.</summary>
        private static X509Certificate2 CreateSelfSignedCertificate()
        {
            using RSA rsa = RSA.Create(2048);
            CertificateRequest request = new(
                "CN=localhost",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1
            );

            SubjectAlternativeNameBuilder san = new();
            san.AddDnsName("localhost");
            request.CertificateExtensions.Add(san.Build());
            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(false, false, 0, false)
            );
            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                    false
                )
            );
            request.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension([new("1.3.6.1.5.5.7.3.1")], false)
            );

            using X509Certificate2 certificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(1)
            );

            byte[] pfx = certificate.Export(X509ContentType.Pfx);
            return X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.Exportable);
        }

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener
                        .AcceptTcpClientAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                _ = HandleClientAsync(client, cancellationToken);
            }
        }

        /// <summary>The client rejected the certificate and aborted the handshake.</summary>
        private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            using (client)
            {
                try
                {
                    using NetworkStream stream = client.GetStream();
                    using SslStream ssl = new(stream, leaveInnerStreamOpen: false);
                    await ssl.AuthenticateAsServerAsync(
                            _certificate,
                            clientCertificateRequired: false,
                            SslProtocols.None,
                            checkCertificateRevocation: false
                        )
                        .ConfigureAwait(false);

                    byte[] buffer = new byte[8192];
                    while (true)
                    {
                        int read = await ssl.ReadAsync(buffer, cancellationToken)
                            .ConfigureAwait(false);
                        if (
                            read == 0
                            || Encoding
                                .ASCII.GetString(buffer, 0, read)
                                .Contains("\r\n\r\n", StringComparison.Ordinal)
                        )
                        {
                            break;
                        }
                    }

                    byte[] bodyBytes = Encoding.UTF8.GetBytes(Body);
                    byte[] headerBytes = Encoding.ASCII.GetBytes(
                        $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n"
                    );

                    await ssl.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);
                    await ssl.WriteAsync(bodyBytes, cancellationToken).ConfigureAwait(false);
                    await ssl.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                    when (ex
                            is IOException
                                or AuthenticationException
                                or OperationCanceledException
                                or SocketException
                    )
                {
                    LastClientFailure = ex;
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync().ConfigureAwait(false);
            _listener.Dispose();
            await _acceptLoop.ConfigureAwait(false);

            _certificate.Dispose();
            _cts.Dispose();
        }
    }

    [Fact]
    public async Task Resolve_ReportsATimeout_WhenTheServerNeverAnswers()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Result<SourceResolveResult> result = await SourceResolver.ResolveAsync(
            $"http://127.0.0.1:{port}/openapi.json",
            insecure: false,
            CancellationToken.None,
            timeout: TimeSpan.FromMilliseconds(300)
        );

        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldContain("timed out", Case.Sensitive);
    }

    [Fact]
    public async Task Resolve_ThrowsCancelled_InsteadOfReportingATimeout_WhenTheCallerCancels()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(300));

        await Should.ThrowAsync<OperationCanceledException>(() =>
            SourceResolver.ResolveAsync(
                $"http://127.0.0.1:{port}/openapi.json",
                insecure: false,
                cancellation.Token
            )
        );
    }

    [Fact]
    public async Task Resolve_ReportsFileNotFoundAsASourceNotFoundFailure()
    {
        Result<SourceResolveResult> result = await SourceResolver.ResolveAsync(
            "./does-not-exist-67890.json",
            CancellationToken.None
        );

        SourceResolver.ClassifyFailure(result.ErrorMessage).ShouldBe(IssueCodes.SourceNotFound);
    }

    [Fact]
    public async Task Resolve_ReportsTheFullPath_ForExistingFile()
    {
        string path = Path.Combine(Path.GetTempPath(), "catalyst-resolver-path-test.json");
        await File.WriteAllTextAsync(path, "{}", TestContext.Current.CancellationToken);

        try
        {
            Result<SourceResolveResult> result = await SourceResolver.ResolveAsync(
                path,
                CancellationToken.None
            );

            result.Value.ShouldNotBeNull().DisplayPath.ShouldBe(Path.GetFullPath(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Resolve_ReportsAFetchFailure_WhenNothingListensOnThePort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        Result<SourceResolveResult> result = await SourceResolver.ResolveAsync(
            $"http://127.0.0.1:{port}/openapi.json",
            CancellationToken.None
        );

        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldContain("Failed to fetch URL", Case.Sensitive);
    }
}
