using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace utf_tab_sharp {
	public static class CpkUnpack {
		public static int main(string[] argv) {
			Console.WriteLine("cpk_unpack " + UtfTab.VERSION);
			Console.WriteLine();
			if (argv.Length != 1) {
				Console.WriteLine("Incorrect program usage");
				Console.WriteLine();
				Console.WriteLine("usage: cpk_unpack-sharp file");
				return -1;
			}

			// open file
			using (FileStream infile = new FileStream(argv[0], FileMode.Open, FileAccess.Read, FileShare.Read)) {
				string base_postfix = "_unpacked";
				string base_name = Path.GetFileName(argv[0]) + base_postfix;

				// get file size
				long file_length = infile.Length;

				analyze_CPK(infile, base_name, file_length);
			}

			return 0;
		}

		public static void analyze_CPK(Stream infile, string base_name, long file_length) {
			const long CpkHeader_offset = 0x0;
			byte[] toc_string_table = null;

			// check header
			{
				byte[] buf = new byte[4];
				byte[] CPK_signature = Encoding.ASCII.GetBytes("CPK ");
				Util.get_bytes_seek(CpkHeader_offset, infile, buf, 4);
				ErrorStuff.CHECK_ERROR(!Util.memcmp(buf, CPK_signature), "CPK signature not found");
			}

			// check CpkHeader
			{
				utf_query_result result = UtfTab.query_utf_nofail(infile, CpkHeader_offset + 0x10, null);

				ErrorStuff.CHECK_ERROR(result.rows != 1, "wrong number of rows in CpkHeader");
			}

			// get TOC offset
			long toc_offset = (long)UtfTab.query_utf_8byte(infile, CpkHeader_offset + 0x10, 0, "TocOffset");

			// get content offset
			long content_offset = (long)UtfTab.query_utf_8byte(infile, CpkHeader_offset + 0x10, 0, "ContentOffset");

			// get file count from CpkHeader
			long CpkHeader_count = UtfTab.query_utf_4byte(infile, CpkHeader_offset + 0x10, 0, "Files");

			// check TOC header
			{
				byte[] buf = new byte[4];
				byte[] TOC_signature = Encoding.ASCII.GetBytes("TOC ");
				Util.get_bytes_seek(toc_offset, infile, buf, 4);
				ErrorStuff.CHECK_ERROR(!Util.memcmp(buf, TOC_signature), "TOC signature not found");
			}

			// get TOC entry count, string table
			long toc_entries;
			{
				utf_query_result result = UtfTab.query_utf_nofail(infile, toc_offset + 0x10, null);

				toc_entries = result.rows;
				toc_string_table = UtfTab.load_utf_string_table(infile, toc_offset + 0x10);
			}

			// check that counts match
			ErrorStuff.CHECK_ERROR(toc_entries != CpkHeader_count, "CpkHeader file count and TOC entry count do not match");

			// extract files
			for (int i = 0; i < toc_entries; i++) {
				// get file name
				string file_name = UtfTab.query_utf_string(infile, toc_offset + 0x10, i,
						"FileName", toc_string_table);

				// get directory name
				string dir_name = UtfTab.query_utf_string(infile, toc_offset + 0x10, i,
						"DirName", toc_string_table);

				// get file size
				long file_size = UtfTab.query_utf_4byte(infile, toc_offset + 0x10, i,
						"FileSize");

				// get extract size
				long extract_size = UtfTab.query_utf_4byte(infile, toc_offset + 0x10, i,
						"ExtractSize");

				// get file offset
				ulong file_offset_raw =
					UtfTab.query_utf_8byte(infile, toc_offset + 0x10, i, "FileOffset");
				if (content_offset < toc_offset) {
					file_offset_raw += (ulong)content_offset;
				} else {
					file_offset_raw += (ulong)toc_offset;
				}

				ErrorStuff.CHECK_ERROR(file_offset_raw > (ulong)long.MaxValue, "File offset too large, will be unable to seek");
				long file_offset = (long)file_offset_raw;

				Console.WriteLine("{0}/{1} 0x{2:x} {3}",
						dir_name, file_name, (ulong)file_offset, file_size);
				using (Stream outfile = Util.open_file_in_directory(base_name, dir_name, '/', file_name, FileMode.Create, FileAccess.ReadWrite, FileShare.None)) {
					ErrorStuff.CHECK_ERRNO(outfile == null, "fopen");

					if (extract_size > file_size) {
						long uncompressed_size =
							CpkUncompress.uncompress(infile, file_offset, file_size, outfile);
						Console.WriteLine("   uncompressed to {0}", uncompressed_size);

						ErrorStuff.CHECK_ERROR(uncompressed_size != extract_size,
								"uncompressed size != ExtractSize");
					} else {
						Util.dump(infile, outfile, file_offset, file_size);
					}
				}
			}

			toc_string_table = null;
		}
	}
}
