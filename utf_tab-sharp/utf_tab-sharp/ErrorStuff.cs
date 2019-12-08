using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace utf_tab_sharp {
	public static class ErrorStuff {
		public static void CHECK_ERROR(bool condition, string message) {
			if (condition) {
				Console.WriteLine(message);
				throw new Exception(message);
			}
		}

		public static void CHECK_ERRNO(bool condition, string message) {
			if (condition) {
				Console.WriteLine(message);
				throw new Exception(message);
			}
		}

		public static void CHECK_FILE(bool condition, Stream file, string message) {
			if (condition) {
				if (file.Position >= file.Length) {
					Console.WriteLine("{0}: unexpected EOF", message);
				} else {
					Console.WriteLine(message);
				}
				throw new Exception(message);
			}
		}
	}
}
