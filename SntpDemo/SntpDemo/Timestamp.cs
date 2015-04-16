using System;
using System.Collections;

namespace SntpDemo
{
	
	//based on http://tools.ietf.org/pdf/rfc4330.pdf

	// NTP timestamp format
	//                     1                   2                   3
	// 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
	//+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
	//|                           Seconds                             |
	//+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
	//|                  Seconds Fraction (0-padded)                  |
	//+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+

	//about the order of bits and bytes in bitarray http://stackoverflow.com/questions/9066831/bitarray-returns-bits-the-wrong-way-around


	public class Timestamp
	{
		public Timestamp(BitArray timestampContents)
		{
			TimestampBitArray = timestampContents;
		}

		public Timestamp(long tickCount)
		{
			Ticks = tickCount;
		}

		private BitArray timestampBitArrayStored { get; set; }

		public BitArray TimestampBitArray
		{
			get
			{
				try
				{
					Byte[] tmp = new Byte[8];
					timestampBitArrayStored.CopyTo(tmp, 0);
					return new BitArray(tmp); //return a deep copy of timestampBitArrayStored
				}
				catch //if there is no stored bitarray yet, then create a new blank one
					//this is needed only if Timestamp is defined as a struct
				{
					Byte[] tmp = new Byte[8];
					for (int i = 0; i < 8; i++)
					{
						tmp[i] = 0;
					}
					timestampBitArrayStored = new BitArray(tmp);
					return new BitArray(tmp);
				}
			}

			private set
			{
				//save the bitarray as-is
				{
					Byte[] tmp = new Byte[8];
					value.CopyTo(tmp, 0);
					timestampBitArrayStored = new BitArray(tmp); //save a deep copy of "value"
				}

				//and update seconds and secondsFraction
				Byte[] secondsConvertedArrayBytes = new Byte[8];
				Byte[] secondsFractionConvertedArrayBytes = new Byte[8];
				Byte[] byteArray = new Byte[8];
				value.CopyTo(byteArray, 0); //convert the BitArray to an array of Bytes
				Byte[] secondsArrayBytes = new Byte[4];
				Byte[] secondsFractionArrayBytes = new Byte[4];
				for (int i = 0; i < 4; i++) //split the byteArray into the two halves
				{
					secondsArrayBytes[i] = byteArray[i];
					secondsFractionArrayBytes[i] = byteArray[i + 4];
				}
				//bigendian->littleendian
				secondsArrayBytes = Helper.SwapEndianness4Bytes(secondsArrayBytes);
				secondsFractionArrayBytes = Helper.SwapEndianness4Bytes(secondsFractionArrayBytes);
				for (int i = 0; i < 4; i++) //copy to 8-byte arrays
				{
					secondsConvertedArrayBytes[i] = secondsArrayBytes[i];
					secondsFractionConvertedArrayBytes[i] = secondsFractionArrayBytes[i];
				}
				for (int i = 0; i < 4; i++) //initialize and zero out the last 4 bytes
				{
					secondsConvertedArrayBytes[i + 4] = 0x00;
					secondsFractionConvertedArrayBytes[i + 4] = 0x00;
				}
				Seconds = BitConverter.ToInt64(secondsConvertedArrayBytes, 0);
				SecondsFraction = BitConverter.ToInt64(secondsFractionConvertedArrayBytes, 0);
			}
		}

		//the seconds part interpreted as uint (cast into long)
		public long Seconds
		{
			get;
			private set;
		} //preserving CLS compliancy (uint is not CLS-compliant)

		//the seconds fraction part interpreted as uint (cast into long)
		public long SecondsFraction
		{
			get;
			private set;
		}  //preserving CLS compliancy (uint is not CLS-compliant)
		public long Ticks //number of 100 nanosecond intervals in the timestamp
		{
			get
			{
				//non-fractional part:
				long ms = Seconds * 1000; //milliseconds
				long us = ms * 1000; //microseconds
				long ticks = us * 10; //100s of nanoseconds

				//fractional part:
				//1 bit of fractional part has value of (bit)*(2^-1)
				//32nd bit of the fractional part has the value of (32nd bit)*(2^-32)
				//The same way, 1 bit number has the value of (bit)*(2^0)
				//The value of the 32bit fraction is (fractionpart)*(2^-32) or (fractionpart)/(2^32)
				//0x0 is 2^0, 0x1 is 2^1, 0x10 is 2^4, 0x100 is 2^8 and so on.
				//Therefore, 2^32 is 0x100000000. Because this is 2 times greater a value than maximal
				//value that can be stored in signed int, it is necessary to denotate long by using L:
				//0x100000000L
				//The value of the fractional part in seconds is secondsFraction / 0x100000000L.
				//The value of the fractional part in ms is secondsFraction / 0x100000000L * 1000.
				//The value of the fractional part in us is secondsFraction / 0x100000000L * 1000 * 1000.
				//The value of the fractional part in ticks is secondsFraction / 0x100000000L * 1000 * 1000 * 10.
				//To prevent problems in implicit casting, it is necessary to reorder the operations.
				//Note that ticks are the smallest distinguishable units, so truncating to whole ticks
				//done by the implicit cast conversion is fine. The result may be off by up to one
				//tick which is not an issue.
				//The value of the fractional part in ticks is (secondsFraction * 1000 * 1000 * 10) / 0x100000000L.
				long ticksFromSecondsFraction = (SecondsFraction * 1000L * 1000L * 10L) / 0x100000000L;

				ticks += ticksFromSecondsFraction; //add the ticks from the fractional part
				return ticks;
			}

			private set
			{
				//compute seconds and secondsFraction
				long ticks = value; //the original value with ticks
				long us = ticks / 10L; //the value in us (note the implicit truncation)
				long ms = us / 1000L; //the value in ms (truncated)
				long secondsWhole = ms / 1000L; //the value in whole seconds, truncated
				long msInWholeSeconds = secondsWhole * 1000L;
				long usInWholeSeconds = msInWholeSeconds * 1000L;
				long ticksInWholeSeconds = usInWholeSeconds * 10L; //we’re back to ticks again with all the ticks belonging to the fractional part stripped off
				long ticksOfFractionalPartOfSecond = ticks - ticksInWholeSeconds; //the remaining fraction of second in ticks

				//the fractional part, if interpreted as a 32bit uint, is multiplied by (2^32) compared to its true meaning
				long ticksFraction = ticksOfFractionalPartOfSecond * 0x100000000L; //multiply by 2^32
				long usFraction = ticksFraction / 10L;
				long msFraction = usFraction / 1000L;
				long sFraction = msFraction / 1000L;

				Seconds = secondsWhole;
				SecondsFraction = sFraction;


				//compute the bitarray

				Byte[] secondsLongBytes = BitConverter.GetBytes(Seconds);
				Byte[] secondsFractionLongBytes = BitConverter.GetBytes(SecondsFraction);

				//now we have two byte arrays each 8 bytes long whereas we need only the first 4 bytes of each array, concatenated

				Byte[] secondsArrayBytes = new Byte[4];
				Byte[] secondsFractionArrayBytes = new Byte[4];
				for (int i = 0; i < 4; i++) //copy in the bytes
				{
					secondsArrayBytes[i] = secondsLongBytes[i];
					secondsFractionArrayBytes[i] = secondsFractionLongBytes[i];
				}
				//littleendian->bigendian
				secondsArrayBytes = Helper.SwapEndianness4Bytes(secondsArrayBytes);
				secondsFractionArrayBytes = Helper.SwapEndianness4Bytes(secondsFractionArrayBytes);


				Byte[] finalBytes = new Byte[8]; //new 64bit array
				for (int i = 0; i < 4; i++) //copy the numbers into the final array
				{
					finalBytes[i] = secondsArrayBytes[i];
					finalBytes[i + 4] = secondsFractionArrayBytes[i];
				}
				timestampBitArrayStored = new BitArray(finalBytes); //the least significant byte is at [0] and the most s. byte is at [7]
				//not saving to timestampBitArray to prevent double recalculation of seconds and secondsFraction.

			}
		}
	} 

}

