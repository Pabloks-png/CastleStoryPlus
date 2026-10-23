using System.IO;
using System.IO.Compression;

namespace CastleStoryPlus.WorldGen;

// Writes 8-bit RGBA PNG files without Unity (so a world can be written off the main thread).
internal static class PngWriter
{
	private static readonly uint[] CrcTable = MakeCrcTable();

	// rgba holds width * height * 4 bytes, the first row at the top of the picture.
	internal static byte[] Encode(byte[] rgba, int width, int height)
	{
		using (MemoryStream png = new MemoryStream())
		{
			png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
			byte[] header = new byte[13];
			WriteInt(header, 0, (uint)width);
			WriteInt(header, 4, (uint)height);
			header[8] = 8;
			header[9] = 6;
			Chunk(png, "IHDR", header);
			Chunk(png, "IDAT", Compress(rgba, width, height));
			Chunk(png, "IEND", new byte[0]);
			return png.ToArray();
		}
	}

	// zlib stream of the rows, each with filter type 0.
	private static byte[] Compress(byte[] rgba, int width, int height)
	{
		int stride = width * 4;
		uint a = 1;
		uint b = 0;
		using (MemoryStream output = new MemoryStream())
		{
			output.WriteByte(0x78);
			output.WriteByte(0x9C);
			using (DeflateStream deflate = new DeflateStream(output, CompressionMode.Compress, true))
			{
				byte[] row = new byte[stride + 1];
				for (int y = 0; y < height; y++)
				{
					row[0] = 0;
					System.Buffer.BlockCopy(rgba, y * stride, row, 1, stride);
					deflate.Write(row, 0, row.Length);
					foreach (byte value in row)
					{
						a = (a + value) % 65521;
						b = (b + a) % 65521;
					}
				}
			}
			byte[] adler = new byte[4];
			WriteInt(adler, 0, (b << 16) | a);
			output.Write(adler, 0, 4);
			return output.ToArray();
		}
	}

	private static void Chunk(Stream png, string type, byte[] data)
	{
		byte[] length = new byte[4];
		WriteInt(length, 0, (uint)data.Length);
		png.Write(length, 0, 4);
		byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
		png.Write(typeBytes, 0, 4);
		png.Write(data, 0, data.Length);
		uint crc = 0xFFFFFFFFu;
		crc = Crc(crc, typeBytes);
		crc = Crc(crc, data);
		byte[] crcBytes = new byte[4];
		WriteInt(crcBytes, 0, crc ^ 0xFFFFFFFFu);
		png.Write(crcBytes, 0, 4);
	}

	private static uint Crc(uint crc, byte[] data)
	{
		foreach (byte value in data)
		{
			crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
		}
		return crc;
	}

	private static uint[] MakeCrcTable()
	{
		uint[] table = new uint[256];
		for (uint n = 0; n < 256; n++)
		{
			uint c = n;
			for (int k = 0; k < 8; k++)
			{
				c = ((c & 1) != 0) ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
			}
			table[n] = c;
		}
		return table;
	}

	private static void WriteInt(byte[] into, int at, uint value)
	{
		into[at] = (byte)(value >> 24);
		into[at + 1] = (byte)(value >> 16);
		into[at + 2] = (byte)(value >> 8);
		into[at + 3] = (byte)value;
	}
}
