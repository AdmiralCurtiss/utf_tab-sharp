using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace utf_tab_sharp {
	public static class UtfView {
		public static int main(string[] argv) {
			Console.WriteLine("utf_view " + UtfTab.VERSION);
			Console.WriteLine();
			if (argv.Length != 1 && argv.Length != 2) {
				Console.WriteLine("Incorrect program usage");
				Console.WriteLine();
				Console.WriteLine("usage: utf_tab-sharp file [offset]");
			}

			long offset = 0;
			if (argv.Length == 2) {
				offset = long.Parse(argv[1]);
			}

			using (Stream infile = new FileStream(argv[0], FileMode.Open, FileAccess.Read, FileShare.Read)) {
				long file_length = infile.Length;
				analyze(infile, offset, file_length);
			}

			return 0;
		}

		public static void analyze(Stream infile, long offset, long file_length) {
			int indent = 0;
			UtfTab.analyze_utf(infile, offset, indent, 1, null);
		}
	}
}
