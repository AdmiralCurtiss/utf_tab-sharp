using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace utf_tab_sharp {
	public static class CpkUncompress {
		const ulong CRILAYLA_sig = 0x4352494C41594C41u;

		// only for up to 16 bits
		public static ushort get_next_bits(Stream infile, ref long offset_p, ref byte bit_pool_p, ref int bits_left_p, int bit_count) {
			ushort out_bits = 0;
			int num_bits_produced = 0;
			while (num_bits_produced < bit_count) {
				if (0 == bits_left_p) {
					bit_pool_p = Util.get_byte_seek(offset_p, infile);
					bits_left_p = 8;
					--offset_p;
				}

				int bits_this_round;
				if (bits_left_p > (bit_count - num_bits_produced)) {
					bits_this_round = bit_count - num_bits_produced;
				} else {
					bits_this_round = bits_left_p;
				}

				out_bits <<= bits_this_round;
				out_bits |= (ushort)(
					(bit_pool_p >> (bits_left_p - bits_this_round)) &
					((1 << bits_this_round) - 1));

				bits_left_p -= bits_this_round;
				num_bits_produced += bits_this_round;
			}

			return out_bits;
		}

		public static long uncompress(Stream infile, long offset, long input_size, Stream outfile) {
			byte[] output_buffer = null;
			ErrorStuff.CHECK_ERROR(!(
				  (Util.get_32_le_seek(offset + 0x00, infile) == 0 &&
				   Util.get_32_le_seek(offset + 0x04, infile) == 0) ||
				  (Util.get_64_be_seek(offset + 0x00, infile) == CRILAYLA_sig)
				), "didn't find 0 or CRILAYLA signature for compressed data");

			long uncompressed_size =
				Util.get_32_le_seek(offset + 0x08, infile);

			long uncompressed_header_offset =
				offset + Util.get_32_le_seek(offset + 0x0C, infile) + 0x10;

			ErrorStuff.CHECK_ERROR(uncompressed_header_offset + 0x100 != offset + input_size, "size mismatch");

			output_buffer = new byte[uncompressed_size + 0x100];
			ErrorStuff.CHECK_ERROR(output_buffer == null, "malloc");

			Util.get_bytes_seek(uncompressed_header_offset, infile, output_buffer, 0x100);

			long input_end = offset + input_size - 0x100 - 1;
			long input_offset = input_end;
			long output_end = 0x100 + uncompressed_size - 1;
			byte bit_pool = 0;
			int bits_left = 0;
			long bytes_output = 0;

			while (bytes_output < uncompressed_size) {
				if (get_next_bits(infile, ref input_offset, ref bit_pool, ref bits_left, 1) != 0) {
					long backreference_offset =
						output_end - bytes_output + get_next_bits(infile, ref input_offset, ref bit_pool, ref bits_left, 13) + 3;
					long backreference_length = 3;

					// decode variable length coding for length
					const int vle_levels = 4;
					int[] vle_lens = new int[] { 2, 3, 5, 8 };
					int vle_level;
					for (vle_level = 0; vle_level < vle_levels; vle_level++) {
						int this_level = get_next_bits(infile, ref input_offset, ref bit_pool, ref bits_left, vle_lens[vle_level]);
						backreference_length += this_level;
						if (this_level != ((1 << vle_lens[vle_level]) - 1)) break;
					}
					if (vle_level == vle_levels) {
						int this_level;
						do {
							this_level = get_next_bits(infile, ref input_offset, ref bit_pool, ref bits_left, 8);
							backreference_length += this_level;
						} while (this_level == 255);
					}

					//printf("0x%08lx backreference to 0x%lx, length 0x%lx\n", output_end-bytes_output, backreference_offset, backreference_length);
					for (int i = 0; i < backreference_length; i++) {
						output_buffer[output_end - bytes_output] = output_buffer[backreference_offset--];
						bytes_output++;
					}
				} else {
					// verbatim byte
					output_buffer[output_end - bytes_output] = (byte)get_next_bits(infile, ref input_offset, ref bit_pool, ref bits_left, 8);
					//printf("0x%08lx verbatim byte\n", output_end-bytes_output);
					bytes_output++;
				}
			}

			Util.put_bytes_seek(0, outfile, output_buffer, 0x100 + uncompressed_size);
			output_buffer = null;

			return 0x100 + bytes_output;
		}
	}
}
