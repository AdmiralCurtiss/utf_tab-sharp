using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace utf_tab_sharp {
	public static class CpkCompress {
		// the CPK compression -- or CRILAYLA, if we name it after its magic bytes -- is a very strange
		// but not particularly complicated beast. its compression mechanism consists solely of referencing
		// and re-outputting bytes that have already been previously output

		// the format is as follows:
		// - 8 bytes: magic 'CRILAYLA'
		// - uint32 little endian: uncompressed size of the following compressed data block
		// - uint32 little endian: compressed size of the following compressed data block; seems to always be 4 byte aligned
		// - variable length: the compressed data block, see below for details
		// - 256 bytes: the first 256 bytes of the input file, uncompressed

		// the first 256 bytes of a file compressed by CRILAYLA are not compressed at all and just copied as-is
		// the reason for this is unclear, but perhaps some games rely on being able to read file headers without
		// having to go through decompression of the file?
		// the rest of the file is compressed into the variable length compressed data block, starting from the end
		// of the file (yes really) and going backwards until we reach the end of the 256 byte block
		// due to this format, CRILAYLA cannot compress files that are less than 256 bytes in length

		// the compressed data block is a bitstream that is read *in reverse*
		// that is, you start at the last byte in the data block, and walk backwards when you need the next byte
		// the individual bits of a byte are to be read starting from the most significant going to the least significant
		// so a byte of 0x80 would be a 1 followed by seven 0s
		// the output stream is also written in reverse, so you start writing the decoded data at the last byte

		// there are two operations available, each encoded in a single bit:

		// operation '0' is a verbatim byte
		// the following 8 bits are to be read and stored into the output stream directly as a byte
		// like the bitstream, the byte is written starting from the most significant bit, so if the 8 bits
		// happen to align with the byte boundary the byte would look the same in the input and output stream
		// to output a single arbitrary byte we thus need 9 bits

		// operation '1' is a backreference

		// each individual integer here in this operation is to be written starting from its most significant bit
		// so for the eg. 3 bit integer, the first bit read from the bitstream is the bit at position 2 of the final integer,
		// the next bit is the bit at position 1, and the third bit is the least significant at position 0

		// first, 13 bits are to be read as an integer for the offset (from the current output location) to where we should
		// start reading bytes to copy to our output stream
		// there is also an implicit addition of 3 to the number read, so the minimum offset is 3 and the maximum offset is 8194
		// after that, a variable amount of bits describes how many bytes are to be copied using this backreference
		// this works as follows:
		// 2 bits are read as an integer
		// if all read bits are 1s read another 3 bits as an integer
		// if all read bits are 1s read another 5 bits as an integer
		// if all read bits are 1s read another 8 bits as an integer; repeat this 8 bit read as long as all bits are 1s
		// once any 0 is encountered in any of the read integers stop reading further integers and add all read integers together
		// (along with once again an implicit 3) to form the amount of bytes to be copied
		// this means a minimum of 3 bits need to be copied, but there is no theoretical maximum -- the practical one depends on the implementation
		// note that this length may be bigger than the actual bytes currently available at the target location!
		// this is legal, as we will produce bytes during this operation that will be then once again read and copied
		// in practical terms, this is basically just a simple loop like this:
		// while (length--) { *output = *(output + offset); --output; }

		// reminder again that all of this is backwards from what you would normally expect since we build the output stream starting from the end!
		// so the 'backreference' actually references bytes that appear later in the file if you were to read the file front-to-back as normal

		// there is no command to end the processing
		// processing automatically stops when the number of uncompressed bytes given in the header have been produced

		private class CrilaylaBitstream {
			private MemoryStream Bytestream = new MemoryStream();
			private uint BitAccumulator = 0;
			private int AccumulatorLength = 0;

			public void PushBit(uint bit) {
				BitAccumulator = (BitAccumulator << 1) | (bit & 0x1);
				AccumulatorLength += 1;
				FlushFullBytes();
			}

			public void PushBits2(uint bits) {
				BitAccumulator = (BitAccumulator << 2) | (bits & 0x3);
				AccumulatorLength += 2;
				FlushFullBytes();
			}

			public void PushBits3(uint bits) {
				BitAccumulator = (BitAccumulator << 3) | (bits & 0x7);
				AccumulatorLength += 3;
				FlushFullBytes();
			}

			public void PushBits5(uint bits) {
				BitAccumulator = (BitAccumulator << 5) | (bits & 0x1f);
				AccumulatorLength += 5;
				FlushFullBytes();
			}

			public void PushBits8(uint bits) {
				BitAccumulator = (BitAccumulator << 8) | (bits & 0xff);
				AccumulatorLength += 8;
				FlushFullBytes();
			}

			public void PushBits13(uint bits) {
				BitAccumulator = (BitAccumulator << 13) | (bits & 0x1fff);
				AccumulatorLength += 13;
				FlushFullBytes();
			}

			private void FlushFullBytes() {
				int l = AccumulatorLength;
				while (l >= 8) {
					l -= 8;
					Bytestream.WriteByte((byte)((BitAccumulator >> l) & 0xff));
				}
				AccumulatorLength = l;
			}

			public MemoryStream Finalize() {
				while (AccumulatorLength > 0) {
					PushBit(0);
				}
				while ((Bytestream.Length % 4) != 0) {
					Bytestream.WriteByte(0);
				}
				return Bytestream;
			}
		}

		private static void WriteBackrefLength(CrilaylaBitstream output, long len) {
			len -= 3;

			if (len < 3) {
				output.PushBits2((uint)len);
				return;
			}
			output.PushBits2(3);
			len -= 3;

			if (len < 7) {
				output.PushBits3((uint)len);
				return;
			}
			output.PushBits3(7);
			len -= 7;

			if (len < 31) {
				output.PushBits5((uint)len);
				return;
			}
			output.PushBits5(31);
			len -= 31;

			while (true) {
				if (len < 255) {
					output.PushBits8((uint)len);
					return;
				}
				output.PushBits8(255);
				len -= 255;
			}
		}

		private static void FindLongestBackreference(byte[] data, long pos, out ushort where, out long length) {
			where = 0;
			length = 0;
			for (long i = 0; i < 0x2000; ++i) {
				long backrefStart = pos + i + 3;
				if (backrefStart >= data.Length) {
					return;
				}

				long backrefOffset = backrefStart;
				long dataOffset = pos;
				while (dataOffset > 0 && data[dataOffset] == data[backrefOffset]) {
					--dataOffset;
					--backrefOffset;
				}

				long backrefLength = pos - dataOffset;
				if (backrefLength > length) {
					length = backrefLength;
					where = (ushort)i;
				}
			}
		}

		public static long compress(Stream infile, long offset, long length, Stream outfile) {
			if (length <= 0x100) {
				throw new Exception("data too short, can't compress this");
			}

			// read bytes to be compressed into buffers
			long dataLength = length - 0x100;
			byte[] uncompressedData = new byte[0x100];
			byte[] input = new byte[dataLength];
			Util.get_bytes_seek(offset, infile, uncompressedData, 0x100);
			Util.get_bytes(infile, input, dataLength);
			long currentPosition = input.LongLength - 1;

			// compress
			CrilaylaBitstream output = new CrilaylaBitstream();
			while (currentPosition >= 0) {
				ushort backrefPos;
				long backrefLen;
				FindLongestBackreference(input, currentPosition, out backrefPos, out backrefLen);
				if (backrefLen >= 3) {
					output.PushBit(1);
					output.PushBits13(backrefPos);
					WriteBackrefLength(output, backrefLen);
					currentPosition -= backrefLen;
				} else {
					output.PushBit(0);
					output.PushBits8(input[currentPosition]);
					--currentPosition;
				}
			}

			MemoryStream ms = output.Finalize();
			long compressedLength = ms.Length;

			// write header
			outfile.WriteByte((byte)((CpkUncompress.CRILAYLA_sig >> 56) & 0xff));
			outfile.WriteByte((byte)((CpkUncompress.CRILAYLA_sig >> 48) & 0xff));
			outfile.WriteByte((byte)((CpkUncompress.CRILAYLA_sig >> 40) & 0xff));
			outfile.WriteByte((byte)((CpkUncompress.CRILAYLA_sig >> 32) & 0xff));
			outfile.WriteByte((byte)((CpkUncompress.CRILAYLA_sig >> 24) & 0xff));
			outfile.WriteByte((byte)((CpkUncompress.CRILAYLA_sig >> 16) & 0xff));
			outfile.WriteByte((byte)((CpkUncompress.CRILAYLA_sig >> 8) & 0xff));
			outfile.WriteByte((byte)((CpkUncompress.CRILAYLA_sig) & 0xff));
			outfile.WriteByte((byte)((dataLength) & 0xff));
			outfile.WriteByte((byte)((dataLength >> 8) & 0xff));
			outfile.WriteByte((byte)((dataLength >> 16) & 0xff));
			outfile.WriteByte((byte)((dataLength >> 24) & 0xff));
			outfile.WriteByte((byte)((compressedLength) & 0xff));
			outfile.WriteByte((byte)((compressedLength >> 8) & 0xff));
			outfile.WriteByte((byte)((compressedLength >> 16) & 0xff));
			outfile.WriteByte((byte)((compressedLength >> 24) & 0xff));

			// write compressed data
			byte[] buffer = ms.GetBuffer();
			long bufferpos = compressedLength - 1;
			while (bufferpos >= 0) {
				outfile.WriteByte(buffer[bufferpos]);
				--bufferpos;
			}

			// write uncompressed data
			outfile.Write(uncompressedData, 0, 0x100);

			// and we're done!
			return compressedLength + 0x110;
		}
	}
}
