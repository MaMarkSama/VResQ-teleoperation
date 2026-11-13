using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VResQ.Video
{
    // Content-Length preserves JPEG bytes and the metadata belonging to that JPEG.
    public sealed class VResqMjpegReader
    {
        public sealed class Frame
        {
            public byte[] jpeg;
            public string metadata;
        }

        private readonly Stream stream;
        private readonly byte[] buffer = new byte[8192];
        private int position;
        private int available;

        public VResqMjpegReader(Stream input) { stream = input; }

        private async Task<string> ReadLineAsync(CancellationToken token)
        {
            using (var line = new MemoryStream())
            {
                while (true)
                {
                    if (position >= available)
                    {
                        available = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                        position = 0;
                        if (available == 0) throw new EndOfStreamException("Camera stream ended");
                    }
                    int newline = Array.IndexOf(buffer, (byte)10, position, available - position);
                    int count = (newline >= 0 ? newline : available) - position;
                    if (line.Length + count > 65536) throw new InvalidDataException("Camera header exceeds 64 KB");
                    line.Write(buffer, position, count);
                    position += count;
                    if (newline >= 0)
                    {
                        position++;
                        return Encoding.ASCII.GetString(line.ToArray()).TrimEnd('\r');
                    }
                }
            }
        }

        public async Task<Frame> ReadAsync(CancellationToken token)
        {
            int length = 0;
            int headerBytes = 0;
            string metadata = null;
            while (true)
            {
                string line = await ReadLineAsync(token);
                headerBytes += line.Length + 2;
                if (headerBytes > 65536) throw new InvalidDataException("Camera headers exceed 64 KB");
                if (line.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase) && !line.Contains(" 200 "))
                    throw new InvalidDataException("Camera HTTP request failed");
                if (line.Length == 0 && length > 0) break;
                int separator = line.IndexOf(':');
                if (separator < 0) continue;
                string key = line.Substring(0, separator);
                string value = line.Substring(separator + 1).Trim();
                if (key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(value, out length) || length <= 0 || length > 12 * 1024 * 1024)
                        throw new InvalidDataException("Invalid camera frame length");
                }
                if (key.Equals("X-VResQ-Meta", StringComparison.OrdinalIgnoreCase))
                    metadata = Encoding.UTF8.GetString(Convert.FromBase64String(value));
            }
            byte[] jpeg = new byte[length];
            int copied = 0;
            while (copied < length)
            {
                if (position >= available)
                {
                    available = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                    position = 0;
                    if (available == 0) throw new EndOfStreamException("Camera closed mid-frame");
                }
                int count = Math.Min(length - copied, available - position);
                Buffer.BlockCopy(buffer, position, jpeg, copied, count);
                position += count;
                copied += count;
            }
            if (length < 4 || jpeg[0] != 255 || jpeg[1] != 216 || jpeg[length - 2] != 255 || jpeg[length - 1] != 217)
                throw new InvalidDataException("Camera payload is not a complete JPEG");
            return new Frame { jpeg = jpeg, metadata = metadata };
        }
    }
}
