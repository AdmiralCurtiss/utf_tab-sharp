using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace utf_tab_sharp {
	public class utf_query {
		public string name;
		public int index;
	}

	public struct offset_size_pair {
		public uint offset;
		public uint size;
	}

	public class utf_query_result {
		public int valid;  // table is valid
		public int found;
		public int type;   // one of COLUMN_TYPE_*

		private ulong internal_value;
		public ulong value_u64 { get { return internal_value; } set { internal_value = value; } }
		public uint value_u32 { get { return (uint)internal_value; } set { internal_value = value; } }
		public ushort value_u16 { get { return (ushort)internal_value; } set { internal_value = value; } }
		public byte value_u8 { get { return (byte)internal_value; } set { internal_value = value; } }
		public float value_float { get { return Util.reinterpret_to_float((uint)internal_value); } set { internal_value = Util.reinterpret_to_uint(value); } }
		public offset_size_pair value_data {
			get { return new offset_size_pair() { offset = (uint)((internal_value >> 32) & 0xFFFFFFFFu), size = (uint)(internal_value & 0xFFFFFFFFu) }; }
			set { internal_value = (((ulong)value.offset) << 32) | ((ulong)value.size); }
		}
		public uint value_string { get { return (uint)internal_value; } set { internal_value = value; } }

		// info for the queried table
		public uint rows;
		public uint name_offset;
		public uint string_table_offset;
		public uint data_offset;
	}

	public class utf_column_info {
		public byte type;
		public uint column_name; // references index in string_table
		public long constant_offset;
	}

	public class utf_table_info {
		public long table_offset;
		public uint table_size;
		public uint schema_offset;
		public uint rows_offset;
		public uint string_table_offset;
		public uint data_offset;
		public byte[] string_table;
		public uint table_name; // references index in string_table
		public ushort columns;
		public ushort row_width;
		public uint rows;

		public utf_column_info[] schema;
	}

	public static class UtfTab {
		// common version across the suite
		public const string VERSION = "0.7 beta 3 [C# port]";

		public const int INDENT_LEVEL = 2;

		public const byte COLUMN_STORAGE_MASK = 0xf0;
		public const byte COLUMN_STORAGE_PERROW = 0x50;
		public const byte COLUMN_STORAGE_CONSTANT = 0x30;
		public const byte COLUMN_STORAGE_ZERO = 0x10;

		// I suspect that "type 2" is signed
		public const byte COLUMN_TYPE_MASK = 0x0f;
		public const byte COLUMN_TYPE_DATA = 0x0b;
		public const byte COLUMN_TYPE_STRING = 0x0a;
		// 0x09 double?
		public const byte COLUMN_TYPE_FLOAT = 0x08;
		// 0x07 signed 8byte?
		public const byte COLUMN_TYPE_8BYTE = 0x06;
		public const byte COLUMN_TYPE_4BYTE2 = 0x05;
		public const byte COLUMN_TYPE_4BYTE = 0x04;
		public const byte COLUMN_TYPE_2BYTE2 = 0x03;
		public const byte COLUMN_TYPE_2BYTE = 0x02;
		public const byte COLUMN_TYPE_1BYTE2 = 0x01;
		public const byte COLUMN_TYPE_1BYTE = 0x00;

		public static string ReadString(byte[] string_table, uint offset) {
			int count = 0;
			for (uint i = offset; i < string_table.Length; ++i) {
				if (string_table[i] == 0)
					break;
				++count;
			}
			return Encoding.UTF8.GetString(string_table, (int)offset, count);
		}

		public static utf_query_result analyze_utf(Stream infile, long offset, int indent, int print, utf_query query) {
			byte[] buf = new byte[4];
			utf_table_info table_info = new utf_table_info();
			byte[] string_table = null;
			utf_column_info[] schema = null;
			utf_query_result result = new utf_query_result();

			result.valid = 0;

			if (print != 0) {
				Util.printf_indent(indent);
				Console.WriteLine("{");
			}

			indent += INDENT_LEVEL;

			table_info.table_offset = offset;

			// check header
			byte[] UTF_signature = Encoding.ASCII.GetBytes("@UTF");
			Util.get_bytes_seek(offset, infile, buf, 4);
			if (!Util.memcmp(buf, UTF_signature)) {
				if (print != 0) {
					Util.printf_indent(indent);
					Console.WriteLine("not a @UTF table at {0:X8}", offset);
				}

				indent -= INDENT_LEVEL;
				if (print != 0) {
					Util.printf_indent(indent);
					Console.WriteLine("}");
				}

				string_table = null;
				schema = null;

				return result;
			}

			// get table size
			table_info.table_size = Util.get_32_be(infile);

			table_info.schema_offset = 0x20;
			table_info.rows_offset = Util.get_32_be(infile);
			table_info.string_table_offset = Util.get_32_be(infile);
			table_info.data_offset = Util.get_32_be(infile);
			uint table_name_string = Util.get_32_be(infile);
			table_info.columns = Util.get_16_be(infile);
			table_info.row_width = Util.get_16_be(infile);
			table_info.rows = Util.get_32_be(infile);

			// allocate for string table
			long string_table_size =
				table_info.data_offset - table_info.string_table_offset;
			string_table = new byte[string_table_size + 1];
			table_info.string_table = string_table;

			// load schema
			schema = new utf_column_info[table_info.columns];
			for (int i = 0; i < schema.Length; ++i) {
				schema[i] = new utf_column_info();
			}
			{
				int i;
				for (i = 0; i < table_info.columns; i++) {
					schema[i].type = Util.get_byte(infile);
					schema[i].column_name = Util.get_32_be(infile);

					if ((schema[i].type & COLUMN_STORAGE_MASK) == COLUMN_STORAGE_CONSTANT) {
						schema[i].constant_offset = infile.Position;
						switch (schema[i].type & COLUMN_TYPE_MASK) {
							case COLUMN_TYPE_STRING:
								Util.get_32_be(infile);
								break;
							case COLUMN_TYPE_8BYTE:
							case COLUMN_TYPE_DATA:
								Util.get_32_be(infile);
								Util.get_32_be(infile);
								break;
							case COLUMN_TYPE_FLOAT:
							case COLUMN_TYPE_4BYTE2:
							case COLUMN_TYPE_4BYTE:
								Util.get_32_be(infile);
								break;
							case COLUMN_TYPE_2BYTE2:
							case COLUMN_TYPE_2BYTE:
								Util.get_16_be(infile);
								break;
							case COLUMN_TYPE_1BYTE2:
							case COLUMN_TYPE_1BYTE:
								Util.get_byte(infile);
								break;
							default:
								ErrorStuff.CHECK_ERROR(true, "unknown type for constant");
								break;
						}
					}
				}
			}

			table_info.schema = schema;

			// read string table
			Util.get_bytes_seek(table_info.string_table_offset + 8 + offset,
					infile, string_table, string_table_size);
			table_info.table_name = table_name_string;

			// fill in the default stuff
			result.valid = 1;
			result.found = 0;
			result.rows = table_info.rows;
			result.name_offset = table_name_string;
			result.string_table_offset = table_info.string_table_offset;
			result.data_offset = table_info.data_offset;

			// explore the values
			if (query != null || print != 0) {
				int i, j;

				for (i = 0; i < table_info.rows; i++) {
					if (print == 0 && query != null && i != query.index) continue;

					long row_offset =
						table_info.table_offset + 8 + table_info.rows_offset +
						i * table_info.row_width;
					long row_start_offset = row_offset;

					if (print != 0) {
						Util.printf_indent(indent);
						Console.WriteLine("{0}[{1}] = {{", ReadString(table_info.string_table, table_info.table_name), i);
					}
					indent += INDENT_LEVEL;
					for (j = 0; j < table_info.columns; j++) {
						byte type = table_info.schema[j].type;
						long constant_offset = table_info.schema[j].constant_offset;
						int constant = 0;

						int qthis = (query != null && i == query.index &&
								ReadString(table_info.string_table, table_info.schema[j].column_name) == query.name) ? 1 : 0;

						if (print != 0) {
							Util.printf_indent(indent);
							Console.Write("{0:x8} {1:x2} {2} = ", row_offset - row_start_offset, type, ReadString(table_info.string_table, table_info.schema[j].column_name));
						}

						if (qthis != 0) {
							result.found = 1;
							result.type = schema[j].type & COLUMN_TYPE_MASK;
						}

						switch (schema[j].type & COLUMN_STORAGE_MASK) {
							case COLUMN_STORAGE_PERROW:
								break;
							case COLUMN_STORAGE_CONSTANT:
								constant = 1;
								break;
							case COLUMN_STORAGE_ZERO:
								if (print != 0) {
									Console.WriteLine("UNDEFINED");
								}
								if (qthis != 0) {
									result.value_u64 = 0;
								}
								continue;
							default:
								ErrorStuff.CHECK_ERROR(true, "unknown storage class");
								break;
						}

						if (true) {
							long data_offset;
							int bytes_read = 0;

							if (constant != 0) {
								data_offset = constant_offset;
								if (print != 0) {
									Console.Write("constant ");
								}
							} else {
								data_offset = row_offset;
							}

							switch (type & COLUMN_TYPE_MASK) {
								case COLUMN_TYPE_STRING: {
									uint string_offset;
									string_offset = Util.get_32_be_seek(data_offset, infile);
									bytes_read = 4;
									if (print != 0) {
										Console.WriteLine("\"{0}\"", ReadString(table_info.string_table, string_offset));
									}
									if (qthis != 0) {
										result.value_string = string_offset;
									}
								}
								break;
								case COLUMN_TYPE_DATA: {
									uint vardata_offset, vardata_size;

									vardata_offset = Util.get_32_be_seek(data_offset, infile);
									vardata_size = Util.get_32_be(infile);
									bytes_read = 8;
									if (print != 0) {
										Console.Write("[0x{0:x8}]", vardata_offset);
										Console.WriteLine(" (size 0x{0:x8})", vardata_size);
									}
									if (qthis != 0) {
										result.value_data = new offset_size_pair() { offset = vardata_offset, size = vardata_size };
									}

									if (vardata_size != 0 && print != 0) {
										// assume that the data is another table
										analyze_utf(infile,
												table_info.table_offset + 8 +
												table_info.data_offset +
												vardata_offset,
												indent,
												print,
												null
												);
									}
								}
								break;

								case COLUMN_TYPE_8BYTE: {
									ulong value =
										Util.get_64_be_seek(data_offset, infile);
									if (print != 0) {
										Console.WriteLine("0x{0:x}", value);
									}
									if (qthis != 0) {
										result.value_u64 = value;
									}
									bytes_read = 8;
									break;
								}
								case COLUMN_TYPE_4BYTE2:
								case COLUMN_TYPE_4BYTE:
									if ((type & COLUMN_TYPE_MASK) == COLUMN_TYPE_4BYTE2 && print != 0) {
										Console.Write("type 2 ");
									} {
										uint value =
											Util.get_32_be_seek(data_offset, infile);
										if (print != 0) {
											Console.WriteLine("{0}", value);
										}
										if (qthis != 0) {
											result.value_u32 = value;
										}
										bytes_read = 4;
									}
									break;
								case COLUMN_TYPE_2BYTE2:
								case COLUMN_TYPE_2BYTE:
									if ((type & COLUMN_TYPE_MASK) == COLUMN_TYPE_2BYTE2 && print != 0) {
										Console.Write("type 2 ");
									} {
										ushort value =
											Util.get_16_be_seek(data_offset, infile);
										if (print != 0) {
											Console.WriteLine("{0}", value);
										}
										if (qthis != 0) {
											result.value_u16 = value;
										}
										bytes_read = 2;
									}
									break;
								case COLUMN_TYPE_FLOAT:
									if (true) {
										uint int_float;
										int_float = Util.get_32_be_seek(data_offset, infile);
										if (print != 0) {
											Console.WriteLine("{0}", Util.reinterpret_to_float(int_float));
										}
										if (qthis != 0) {
											result.value_u32 = int_float;
										}
									}
									bytes_read = 4;
									break;
								case COLUMN_TYPE_1BYTE2:
								case COLUMN_TYPE_1BYTE:
									if ((type & COLUMN_TYPE_MASK) == COLUMN_TYPE_1BYTE2 && print != 0) {
										Console.Write("type 2 ");
									} {
										byte value =
											Util.get_byte_seek(data_offset, infile);
										if (print != 0) {
											Console.WriteLine("{0}", value);
										}
										if (qthis != 0) {
											result.value_u8 = value;
										}
										bytes_read = 1;
									}
									break;
								default:
									ErrorStuff.CHECK_ERROR(true, "unknown normal type");
									break;
							}

							if (constant == 0) {
								row_offset += bytes_read;
							}
						} // useless if end
					} // column for loop end
					indent -= INDENT_LEVEL;
					if (print != 0) {
						Util.printf_indent(indent);
						Console.WriteLine("}");
					}

					ErrorStuff.CHECK_ERROR(row_offset - row_start_offset != table_info.row_width,
							"column widths do now add up to row width");

					if (query != null && print == 0 && i >= query.index) break;
				} // row for loop end
			} // explore values block end

			indent -= INDENT_LEVEL;
			if (print != 0) {
				Util.printf_indent(indent);
				Console.WriteLine("}");
			}

			string_table = null;
			schema = null;

			return result;
		}

		public static utf_query_result query_utf(Stream infile, long offset, utf_query query) {
			return analyze_utf(infile, offset, 0, 0, query);
		}

		public static utf_query_result query_utf_nofail(Stream infile, long offset, utf_query query) {
			utf_query_result result = query_utf(infile, offset, query);

			ErrorStuff.CHECK_ERROR(result.valid == 0, "didn't find valid @UTF table where one was expected");
			ErrorStuff.CHECK_ERROR(query != null && result.found == 0, "key not found");

			return result;
		}
		public static utf_query_result query_utf_key(Stream infile, long offset, int index, string name) {
			utf_query query = new utf_query();
			query.index = index;
			query.name = name;

			return query_utf_nofail(infile, offset, query);
		}

		public static ulong query_utf_8byte(Stream infile, long offset, int index, string name) {
			utf_query_result result = query_utf_key(infile, offset, index, name);
			ErrorStuff.CHECK_ERROR(result.type != COLUMN_TYPE_8BYTE, "value is not an 8 byte uint");
			return result.value_u64;
		}

		public static uint query_utf_4byte(Stream infile, long offset, int index, string name) {
			utf_query_result result = query_utf_key(infile, offset, index, name);
			ErrorStuff.CHECK_ERROR(result.type != COLUMN_TYPE_4BYTE, "value is not a 4 byte uint");
			return result.value_u32;
		}

		public static ushort query_utf_2byte(Stream infile, long offset, int index, string name) {
			utf_query_result result = query_utf_key(infile, offset, index, name);
			ErrorStuff.CHECK_ERROR(result.type != COLUMN_TYPE_2BYTE, "value is not a 2 byte uint");
			return result.value_u16;
		}

		public static byte[] load_utf_string_table(Stream infile, long offset) {
			utf_query_result result = query_utf_nofail(infile, offset, null);

			ulong string_table_size = result.data_offset - result.string_table_offset;
			long string_table_offset = offset + 8 + result.string_table_offset;
			byte[] string_table = new byte[string_table_size + 1];

			Util.get_bytes_seek(string_table_offset, infile,
					string_table, (long)string_table_size);

			return string_table;
		}

		public static uint query_utf_string(Stream infile, long offset, int index, string name) {
			utf_query_result result = query_utf_key(infile, offset, index, name);
			ErrorStuff.CHECK_ERROR(result.type != COLUMN_TYPE_STRING, "value is not a string");
			return result.value_string;
		}

		public static string query_utf_string(Stream infile, long offset, int index, string name, byte[] string_table) {
			return ReadString(string_table, query_utf_string(infile, offset, index, name));
		}

		public static offset_size_pair query_utf_data(Stream infile, long offset, int index, string name) {
			utf_query_result result = query_utf_key(infile, offset, index, name);
			ErrorStuff.CHECK_ERROR(result.type != COLUMN_TYPE_DATA, "value is not data");
			return result.value_data;
		}
	}
}
