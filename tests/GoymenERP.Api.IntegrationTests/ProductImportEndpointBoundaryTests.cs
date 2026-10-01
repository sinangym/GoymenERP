using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Security;
using Microsoft.AspNetCore.Http;

namespace GoymenERP.Api.IntegrationTests;

public sealed class ProductImportEndpointBoundaryTests
{
    [Fact]
    public async Task Denied_authority_is_refused_before_declared_size_or_body_is_observed()
    {
        var body = new RefuseReadStream();
        var context = MultipartContext(body, ProductImportRequestBoundary.MultipartBodyLengthLimit + 1);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ProductImportRequestBoundary.ReadAsync(
            context, new Authority(granted: false), apply: false, CancellationToken.None));

        Assert.False(body.WasRead);
    }

    [Fact]
    public async Task Authorized_declared_oversize_is_refused_before_body_read()
    {
        var body = new RefuseReadStream();
        var context = MultipartContext(body, ProductImportRequestBoundary.MultipartBodyLengthLimit + 1);

        await Assert.ThrowsAsync<ProductImportPayloadTooLargeException>(() => ProductImportRequestBoundary.ReadAsync(
            context, new Authority(granted: true), apply: true, CancellationToken.None));

        Assert.False(body.WasRead);
        Assert.True(ProductImportRequestBoundary.MaximumWorkbookBytes
            < ProductImportRequestBoundary.MultipartBodyLengthLimit);
    }

    private static DefaultHttpContext MultipartContext(Stream body, long contentLength)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "multipart/form-data; boundary=product-import-test";
        context.Request.ContentLength = contentLength;
        context.Request.Body = body;
        return context;
    }

    private sealed class Authority(bool granted) : IGroupSharedMasterAuthorizationService
    {
        public Task<GroupSharedMasterAuthorizationSnapshot> GetSnapshotAsync(
            GroupSharedMasterKind subject, GroupSharedMasterOperation operation,
            CancellationToken cancellationToken = default) => Task.FromResult(granted
                ? new GroupSharedMasterAuthorizationSnapshot(true, subject, operation,
                    Guid.Parse("20000000-0000-0000-0000-000000000001"),
                    Guid.Parse("10000000-0000-0000-0000-000000000001"), "product-import-test-v1")
                : GroupSharedMasterAuthorizationSnapshot.Denied(subject, operation));
    }

    private sealed class RefuseReadStream : Stream
    {
        public bool WasRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 0;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count)
        {
            WasRead = true;
            throw new InvalidOperationException("The request body must not be read.");
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            WasRead = true;
            return ValueTask.FromException<int>(new InvalidOperationException("The request body must not be read."));
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
