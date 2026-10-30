using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CastleStoryPlus.Port;

// Writes an uncompressed UnityFS asset bundle (format 6, as Unity 5.6 wrote them): a header, the block and directory
// info, then the files back to back. Uncompressed so Unity can read the files in place.
public static class UnityFsWriter
{
	public sealed class Entry
	{
		public string Name;
		public string SourcePath;
		public byte[] Data;
		public bool Serialized;

		public long Length => Data != null ? Data.Length : new FileInfo(SourcePath).Length;
	}

	private const uint BlockSize = 0x20000;

	public static void Write(string path, string engineVersion, IList<Entry> entries)
	{
		long total = 0;
		foreach (Entry entry in entries)
		{
			total += entry.Length;
		}
		var info = new MemoryStream();
		using (var w = new BigEndianWriter(info))
		{
			w.Write(new byte[16]);
			var blocks = new List<uint>();
			for (long left = total; left > 0; left -= BlockSize)
			{
				blocks.Add((uint)System.Math.Min(left, BlockSize));
			}
			w.Write(blocks.Count);
			foreach (uint size in blocks)
			{
				w.Write(size);
				w.Write(size);
				w.Write((ushort)0);
			}
			w.Write(entries.Count);
			long offset = 0;
			foreach (Entry entry in entries)
			{
				w.Write(offset);
				w.Write(entry.Length);
				w.Write(entry.Serialized ? 4u : 0u);
				w.WriteCString(entry.Name);
				offset += entry.Length;
			}
		}
		byte[] infoBytes = info.ToArray();
		using (var file = new FileStream(path, FileMode.Create, FileAccess.Write))
		using (var w = new BigEndianWriter(file))
		{
			w.WriteCString("UnityFS");
			w.Write(6u);
			w.WriteCString("5.x.x");
			w.WriteCString(engineVersion);
			long sizePos = file.Position;
			w.Write(0L);
			w.Write((uint)infoBytes.Length);
			w.Write((uint)infoBytes.Length);
			// No compression, block and directory info right after the header.
			w.Write(0x40u);
			w.Write(infoBytes);
			foreach (Entry entry in entries)
			{
				if (entry.Data != null)
				{
					w.Write(entry.Data);
				}
				else
				{
					using (var source = File.OpenRead(entry.SourcePath))
					{
						source.CopyTo(file);
					}
				}
			}
			long end = file.Position;
			file.Position = sizePos;
			w.Write(end);
		}
	}

	private sealed class BigEndianWriter : BinaryWriter
	{
		public BigEndianWriter(Stream stream) : base(stream, Encoding.UTF8, true)
		{
		}

		public override void Write(int value) => WriteBig((uint)value, 4);

		public override void Write(uint value) => WriteBig(value, 4);

		public override void Write(ushort value) => WriteBig(value, 2);

		public override void Write(long value)
		{
			WriteBig((uint)(value >> 32), 4);
			WriteBig((uint)value, 4);
		}

		public void WriteCString(string text)
		{
			base.Write(Encoding.UTF8.GetBytes(text));
			base.Write((byte)0);
		}

		private void WriteBig(uint value, int bytes)
		{
			for (int i = bytes - 1; i >= 0; i--)
			{
				base.Write((byte)(value >> (8 * i)));
			}
		}
	}
}
