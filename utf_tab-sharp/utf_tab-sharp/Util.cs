using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace utf_tab_sharp {
	public static class Util {
		public static Stream open_file_in_directory(string base_name, string dir_name, char orig_sep, string file_name, FileMode mode, FileAccess access, FileShare share) {
			string full_name = base_name;
			if (dir_name != null) {
				full_name = Path.Combine(base_name, dir_name);
			}
			Directory.CreateDirectory(full_name);
			full_name = Path.Combine(full_name, file_name);
			return new FileStream(full_name, mode, access, share);
		}

		public static void dump_from_here(Stream input, Stream output, long count) {
			byte[] buffer = new byte[0x800];
			int read;

			long bytesLeft = count;
			while ((read = input.Read(buffer, 0, (int)Math.Min(buffer.LongLength, bytesLeft))) > 0) {
				output.Write(buffer, 0, read);
				bytesLeft -= read;
				if (bytesLeft <= 0) return;
			}
		}

		public static void dump(Stream infile, Stream outfile, long offset, long size) {
			infile.Position = offset;

			dump_from_here(infile, outfile, size);
		}


		public static uint read_32_le(byte[] bytes) {
			uint result = 0;
			for (int i = 3; i >= 0; i--) result = (result << 8) | bytes[i];
			return result;
		}
		public static ushort read_16_le(byte[] bytes) {
			uint result = 0;
			for (int i = 1; i >= 0; i--) result = (result << 8) | bytes[i];
			return (ushort)result;
		}
		public static ulong read_64_be(byte[] bytes) {
			ulong result = 0;
			for (int i = 0; i < 8; i++) result = (result << 8) | bytes[i];
			return result;
		}
		public static uint read_32_be(byte[] bytes) {
			uint result = 0;
			for (int i = 0; i < 4; i++) result = (result << 8) | bytes[i];
			return result;
		}
		public static ushort read_16_be(byte[] bytes) {
			uint result = 0;
			for (int i = 0; i < 2; i++) result = (result << 8) | bytes[i];
			return (ushort)result;
		}

		public static byte get_byte(Stream infile) {
			int b = infile.ReadByte();
			ErrorStuff.CHECK_FILE(b == -1, infile, "fread");
			return (byte)b;
		}
		public static byte get_byte_seek(long offset, Stream infile) {
			infile.Position = offset;
			return get_byte(infile);
		}
		public static ushort get_16_be(Stream infile) {
			byte[] buf = new byte[2];
			int bytes_read = infile.Read(buf, 0, 2);
			ErrorStuff.CHECK_FILE(bytes_read != 2, infile, "fread");
			return read_16_be(buf);
		}
		public static ushort get_16_be_seek(long offset, Stream infile) {
			infile.Position = offset;
			return get_16_be(infile);
		}
		public static ushort get_16_le(Stream infile) {
			byte[] buf = new byte[2];
			int bytes_read = infile.Read(buf, 0, 2);
			ErrorStuff.CHECK_FILE(bytes_read != 2, infile, "fread");
			return read_16_le(buf);
		}
		public static ushort get_16_le_seek(long offset, Stream infile) {
			infile.Position = offset;
			return get_16_le(infile);
		}
		public static uint get_32_be(Stream infile) {
			byte[] buf = new byte[4];
			int bytes_read = infile.Read(buf, 0, 4);
			ErrorStuff.CHECK_FILE(bytes_read != 4, infile, "fread");
			return read_32_be(buf);
		}
		public static uint get_32_be_seek(long offset, Stream infile) {
			infile.Position = offset;
			return get_32_be(infile);
		}
		public static uint get_32_le(Stream infile) {
			byte[] buf = new byte[4];
			int bytes_read = infile.Read(buf, 0, 4);
			ErrorStuff.CHECK_FILE(bytes_read != 4, infile, "fread");
			return read_32_le(buf);
		}
		public static uint get_32_le_seek(long offset, Stream infile) {
			infile.Position = offset;
			return get_32_le(infile);
		}
		public static ulong get_64_be(Stream infile) {
			byte[] buf = new byte[8];
			int bytes_read = infile.Read(buf, 0, 8);
			ErrorStuff.CHECK_FILE(bytes_read != 8, infile, "fread");
			return read_64_be(buf);
		}
		public static ulong get_64_be_seek(long offset, Stream infile) {
			infile.Position = offset;
			return get_64_be(infile);
		}

		public static void get_bytes(Stream infile, byte[] buf, long byte_count) {
			int bytes_read = infile.Read(buf, 0, (int)byte_count);
			ErrorStuff.CHECK_FILE(bytes_read != byte_count, infile, "fread");
		}

		public static void get_bytes_seek(long offset, Stream infile, byte[] buf, long byte_count) {
			infile.Position = offset;
			get_bytes(infile, buf, byte_count);
		}

		public static void put_bytes(Stream outfile, byte[] buf, long byte_count) {
			outfile.Write(buf, 0, (int)byte_count);
		}

		public static void put_bytes_seek(long offset, Stream outfile, byte[] buf, long byte_count) {
			outfile.Position = offset;
			put_bytes(outfile, buf, byte_count);
		}

		public static void fprintf_indent(Stream outfile, int indent) {
			for (int i = 0; i < indent; ++i) {
				outfile.WriteByte((byte)' ');
			}
		}

		public static void printf_indent(int indent) {
			for (int i = 0; i < indent; ++i) {
				Console.Write(" ");
			}
		}

		public static bool memcmp(byte[] a, byte[] b) {
			if (a == null || b == null) {
				return a == b;
			}
			if (a.LongLength != b.LongLength) {
				return false;
			}
			for (long i = 0; i < a.LongLength; ++i) {
				if (a[i] != b[i]) {
					return false;
				}
			}
			return true;
		}

		public static uint reinterpret_to_uint(float f) {
			byte[] bytes = BitConverter.GetBytes(f);
			return BitConverter.ToUInt32(bytes, 0);
		}

		public static float reinterpret_to_float(uint u) {
			byte[] bytes = BitConverter.GetBytes(u);
			return BitConverter.ToSingle(bytes, 0);
		}
	}
}
