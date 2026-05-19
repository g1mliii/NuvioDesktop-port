using System.Buffers;
using System.Net.Http;
using System.Text;
using Nuvio.Core.Validation;

namespace Nuvio.Core.Net;

public static class BoundedHttpResponseReader
{
    private const int BufferSize = 8192;

    public static async Task<string> ReadStringAsync(
        HttpResponseMessage response,
        long maxBytes,
        string resourceLabel,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is { } declared && declared > maxBytes)
        {
            throw new NuvioValidationException(
                $"{resourceLabel} exceeds the configured {maxBytes} byte limit.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await ReadStringAsync(stream, maxBytes, resourceLabel, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<string> ReadStringAsync(
        Stream stream,
        long maxBytes,
        string resourceLabel,
        CancellationToken cancellationToken)
    {
        if (maxBytes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        }

        var rented = ArrayPool<byte>.Shared.Rent(BufferSize);
        using var buffer = new MemoryStream();
        try
        {
            int read;
            while ((read = await stream.ReadAsync(rented.AsMemory(0, rented.Length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > maxBytes)
                {
                    throw new NuvioValidationException(
                        $"{resourceLabel} exceeds the configured {maxBytes} byte limit.");
                }

                buffer.Write(rented, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
