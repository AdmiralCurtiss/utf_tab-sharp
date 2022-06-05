using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace utf_tab_sharp {
	public static class CpkUncompress {
		internal const ulong CRILAYLA_sig = 0x4352494C41594C41u;

		// only for up to 16 bits
		public static ushort get_next_bits(byte[] infile, ref long offset_p, ref byte bit_pool_p, ref int bits_left_p, int bit_count) {
			ushort out_bits = 0;
			int num_bits_produced = 0;
			while (num_bits_produced < bit_count) {
				if (0 == bits_left_p) {
					bit_pool_p = infile[offset_p];
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
			ulong magic = Util.get_64_be_seek(offset + 0x00, infile);
			ErrorStuff.CHECK_ERROR(!((magic == 0) || (magic == CRILAYLA_sig)), "didn't find 0 or CRILAYLA signature for compressed data");

			long uncompressed_size =
				Util.get_32_le_seek(offset + 0x08, infile);

			long uncompressed_header_offset =
				offset + Util.get_32_le_seek(offset + 0x0C, infile) + 0x10;

			ErrorStuff.CHECK_ERROR(uncompressed_header_offset + 0x100 != offset + input_size, "size mismatch");

			byte[] output_buffer = new byte[uncompressed_size + 0x100];
			Util.get_bytes_seek(uncompressed_header_offset, infile, output_buffer, 0x100);

			long buffer_input_size = input_size - 0x100;
			long input_offset = buffer_input_size - 1;
			long output_end = 0x100 + uncompressed_size - 1;
			byte bit_pool = 0;
			int bits_left = 0;
			long bytes_output = 0;
			const int vle_levels = 4;
			const int vle_lens_0 = 2;
			const int vle_lens_1 = 3;
			const int vle_lens_2 = 5;
			const int vle_lens_3 = 8;

			if (buffer_input_size > int.MaxValue) {
				throw new Exception("compressed data too big to load into buffer");
			}
			byte[] input_buffer = new byte[buffer_input_size];
			infile.Position = offset;
			Util.get_bytes_seek(offset, infile, input_buffer, buffer_input_size);

			while (bytes_output < uncompressed_size) {
				if (get_next_bits(input_buffer, ref input_offset, ref bit_pool, ref bits_left, 1) != 0) {
					long backreference_offset =
						output_end - bytes_output + get_next_bits(input_buffer, ref input_offset, ref bit_pool, ref bits_left, 13) + 3;
					long backreference_length = 3;

					// decode variable length coding for length
					int vle_level = 0;
					{
						int this_level = get_next_bits(input_buffer, ref input_offset, ref bit_pool, ref bits_left, vle_lens_0);
						backreference_length += this_level;
						if (this_level != ((1 << vle_lens_0) - 1)) goto vle_levels_done;
						++vle_level;
						this_level = get_next_bits(input_buffer, ref input_offset, ref bit_pool, ref bits_left, vle_lens_1);
						backreference_length += this_level;
						if (this_level != ((1 << vle_lens_1) - 1)) goto vle_levels_done;
						++vle_level;
						this_level = get_next_bits(input_buffer, ref input_offset, ref bit_pool, ref bits_left, vle_lens_2);
						backreference_length += this_level;
						if (this_level != ((1 << vle_lens_2) - 1)) goto vle_levels_done;
						++vle_level;
						this_level = get_next_bits(input_buffer, ref input_offset, ref bit_pool, ref bits_left, vle_lens_3);
						backreference_length += this_level;
						if (this_level != ((1 << vle_lens_3) - 1)) goto vle_levels_done;
						++vle_level;
					}
				vle_levels_done:
					if (vle_level == vle_levels) {
						int this_level;
						do {
							this_level = get_next_bits(input_buffer, ref input_offset, ref bit_pool, ref bits_left, 8);
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
					output_buffer[output_end - bytes_output] = (byte)get_next_bits(input_buffer, ref input_offset, ref bit_pool, ref bits_left, 8);
					//printf("0x%08lx verbatim byte\n", output_end-bytes_output);
					bytes_output++;
				}
			}

			Util.put_bytes_seek(0, outfile, output_buffer, 0x100 + uncompressed_size);

			return 0x100 + bytes_output;
		}
	}
}
